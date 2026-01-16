# Orleans Saga Example

This repository contains a minimal Orleans saga (process manager) example that orchestrates an order/payment flow with compensation logic. The example lives in [`samples/OrderSaga`](samples/OrderSaga/OrderSaga.cs).

To keep things focused on the saga pattern itself, the sample uses single-file grain definitions with simplified in-memory logic. In a real system these grains would call external services or databases.
