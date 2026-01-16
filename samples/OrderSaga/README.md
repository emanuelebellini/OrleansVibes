# Minimal Orleans Saga Sample

This sample demonstrates a simple saga orchestrator grain that coordinates order creation, inventory reservation, and payment capture. The orchestrator compensates earlier steps when something fails.

## Key parts

1. **`OrderSagaGrain`** orchestrates the workflow.
2. **`OrderServiceGrain`**, **`InventoryServiceGrain`**, and **`PaymentServiceGrain`** model the domain services.
3. Compensation happens by releasing inventory and refunding payments when downstream steps fail.

The saga flow is:

1. Create a pending order state.
2. Reserve inventory for the product.
3. Charge the customer.
4. Confirm the order.

If steps 2-4 throw, the saga calls the corresponding compensation methods to keep the system consistent.

## Usage from a client grain or Orleans timer

```csharp
public class CheckoutGrain : Grain, ICheckoutGrain
{
    public async Task<OrderSagaResult> CheckoutAsync(Guid customerId, Guid productId)
    {
        var saga = GrainFactory.GetGrain<IOrderSagaGrain>(Guid.NewGuid());

        var request = new OrderRequest(
            OrderId: Guid.NewGuid(),
            CustomerId: customerId,
            ProductId: productId,
            Quantity: 1,
            Amount: 25m);

        return await saga.StartAsync(request);
    }
}
```

This shows how the saga grain can be invoked from another grain. Hosting configuration and persistence providers are omitted for brevity—you can plug this into an Orleans silo configured with a memory-grain-storage provider for the stateful grains.
