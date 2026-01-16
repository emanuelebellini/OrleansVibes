using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;

namespace OrleansSaga;

// Request + response records used by the saga
[GenerateSerializer]
public sealed record OrderRequest(Guid OrderId, Guid CustomerId, Guid ProductId, int Quantity, decimal Amount);

[GenerateSerializer]
public sealed record OrderSagaResult(bool Success, string? ErrorMessage = null);

// Saga entry point grain
public interface IOrderSagaGrain : IGrainWithGuidKey
{
    Task<OrderSagaResult> StartAsync(OrderRequest request);
}

public sealed class OrderSagaGrain : Grain, IOrderSagaGrain
{
    public async Task<OrderSagaResult> StartAsync(OrderRequest request)
    {
        var orderGrain = GrainFactory.GetGrain<IOrderServiceGrain>(request.OrderId);
        var inventoryGrain = GrainFactory.GetGrain<IInventoryServiceGrain>(request.ProductId);
        var paymentGrain = GrainFactory.GetGrain<IPaymentServiceGrain>(request.CustomerId);

        // 1. Create a pending order state
        await orderGrain.CreatePendingAsync(request);

        try
        {
            // 2. Reserve inventory (compensated by ReleaseReservationAsync)
            await inventoryGrain.ReserveInventoryAsync(request.Quantity);

            try
            {
                // 3. Charge the customer (compensated by RefundAsync)
                await paymentGrain.ChargeAsync(request.Amount);
            }
            catch (Exception chargeException)
            {
                await inventoryGrain.ReleaseReservationAsync(request.Quantity);
                await orderGrain.FailAsync($"Payment failed: {chargeException.Message}");
                return new OrderSagaResult(false, chargeException.Message);
            }
        }
        catch (Exception reserveException)
        {
            await orderGrain.FailAsync($"Inventory reservation failed: {reserveException.Message}");
            return new OrderSagaResult(false, reserveException.Message);
        }

        try
        {
            // 4. Confirm order after successful steps
            await orderGrain.ConfirmAsync();
            return new OrderSagaResult(true);
        }
        catch (Exception confirmationException)
        {
            // Compensation across services to keep state consistent
            await paymentGrain.RefundAsync(request.Amount);
            await inventoryGrain.ReleaseReservationAsync(request.Quantity);
            await orderGrain.FailAsync($"Confirmation failed: {confirmationException.Message}");
            return new OrderSagaResult(false, confirmationException.Message);
        }
    }
}

// Domain service grains used by the saga
public interface IOrderServiceGrain : IGrainWithGuidKey
{
    Task CreatePendingAsync(OrderRequest request);
    Task ConfirmAsync();
    Task FailAsync(string reason);
}

public sealed class OrderServiceGrain : Grain<OrderState>, IOrderServiceGrain
{
    public Task CreatePendingAsync(OrderRequest request)
    {
        State = new OrderState
        {
            OrderId = this.GetPrimaryKey(),
            CustomerId = request.CustomerId,
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            Amount = request.Amount,
            Status = "Pending"
        };
        return WriteStateAsync();
    }

    public Task ConfirmAsync()
    {
        State.Status = "Confirmed";
        return WriteStateAsync();
    }

    public Task FailAsync(string reason)
    {
        State.Status = $"Failed: {reason}";
        return WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed record OrderState
{
    [Id(0)] public Guid OrderId { get; init; }
    [Id(1)] public Guid CustomerId { get; init; }
    [Id(2)] public Guid ProductId { get; init; }
    [Id(3)] public int Quantity { get; init; }
    [Id(4)] public decimal Amount { get; init; }
    [Id(5)] public string Status { get; set; } = "Pending";
}

public interface IInventoryServiceGrain : IGrainWithGuidKey
{
    Task ReserveInventoryAsync(int quantity);
    Task ReleaseReservationAsync(int quantity);
}

public sealed class InventoryServiceGrain : Grain<InventoryState>, IInventoryServiceGrain
{
    public override Task OnActivateAsync()
    {
        State ??= new InventoryState { ProductId = this.GetPrimaryKey(), Available = 10 };
        return base.OnActivateAsync();
    }

    public Task ReserveInventoryAsync(int quantity)
    {
        if (State.Available < quantity)
        {
            throw new OrleansException($"Insufficient inventory for product {State.ProductId}");
        }

        State.Available -= quantity;
        State.Reserved += quantity;
        return WriteStateAsync();
    }

    public Task ReleaseReservationAsync(int quantity)
    {
        State.Available += quantity;
        State.Reserved -= quantity;
        return WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed record InventoryState
{
    [Id(0)] public Guid ProductId { get; init; }
    [Id(1)] public int Available { get; set; }
    [Id(2)] public int Reserved { get; set; }
}

public interface IPaymentServiceGrain : IGrainWithGuidKey
{
    Task ChargeAsync(decimal amount);
    Task RefundAsync(decimal amount);
}

public sealed class PaymentServiceGrain : Grain<PaymentState>, IPaymentServiceGrain
{
    public override Task OnActivateAsync()
    {
        State ??= new PaymentState { CustomerId = this.GetPrimaryKey(), Balance = 100m };
        return base.OnActivateAsync();
    }

    public Task ChargeAsync(decimal amount)
    {
        if (State.Balance < amount)
        {
            throw new OrleansException($"Customer {State.CustomerId} has insufficient balance");
        }

        State.Balance -= amount;
        State.Held += amount;
        return WriteStateAsync();
    }

    public Task RefundAsync(decimal amount)
    {
        State.Balance += amount;
        State.Held -= amount;
        return WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed record PaymentState
{
    [Id(0)] public Guid CustomerId { get; init; }
    [Id(1)] public decimal Balance { get; set; }
    [Id(2)] public decimal Held { get; set; }
}
