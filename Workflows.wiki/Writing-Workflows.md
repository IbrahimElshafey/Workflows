# Writing Workflows

Workflows are defined as C# classes that inherit from `WorkflowContainer` and implement a run method returning `IAsyncEnumerable<Wait>`.

---

## 1. Declaring a Workflow

To author a workflow:
1.  Decorate the class with the `[Workflow]` attribute to specify its unique name and version.
2.  Declare the class as `sealed` (enforced by code analyzer).
3.  Add serializable properties on the class to represent your persistent domain state.

```csharp
using Workflows.Definition;

[Workflow("UserOnboardingWorkflow", 1)]
public sealed partial class UserOnboardingWorkflow : WorkflowContainer
{
    // Properties are automatically serialized to JSON and hydrated on resumption
    public string UserId { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }

    public async IAsyncEnumerable<Wait> Run(OnboardingState state)
    {
        UserId = state.UserId;
        
        yield return WaitDelay(TimeSpan.FromDays(1), "Wait for verification");
        // ...
    }
}
```

---

## 2. Core Wait Primitives

Execution is suspended by yielding wait instructions. The engine provides several primitives:

### A. Wait for a Signal (`WaitSignal`)
Suspends execution until an external event of type `TSignal` occurs that satisfies matching criteria:
```csharp
yield return WaitSignal<VerificationEvent>("EmailVerifiedSignal", "Wait for verification")
    .MatchIf((evt) => evt.UserId == UserId)
    .AfterMatch((evt) => {
        EmailVerified = true;
    });
```

### B. Wait for a Duration (`WaitDelay`)
Suspends execution for a relative timeframe (represented as a timer):
```csharp
yield return WaitDelay(TimeSpan.FromHours(24), "Wait 1 day");
```

### C. Wait Group / Parallel Execution (`WaitGroup`)
Waits for multiple instructions concurrently. You can define compound matching constraints (e.g. MatchAll, MatchAny):
```csharp
yield return WaitGroup([
    WaitSignal<StockConfirmedEvent>("StockCheck", "WaitStock"),
    WaitSignal<PaymentAuthorizedEvent>("PaymentCheck", "WaitPayment")
], "Order Fulfillment Group");
```

### D. Sub-Workflows (`WaitSubWorkflow`)
Invokes another nested workflow. The main workflow suspends until the sub-workflow run is completed:
```csharp
yield return WaitSubWorkflow(FulfillmentSubWorkflow(), "Run fulfillment sub-process");

[SubWorkflow]
private async IAsyncEnumerable<Wait> FulfillmentSubWorkflow()
{
    yield return WaitSignal<ShippingDispatchedEvent>("CourierPickup", "Wait Courier");
}
```

---

## 3. Strict No-Closure Enforcement (Important)

Because lambda expressions (such as `MatchIf` and `AfterMatch` expressions) are compiled into memory caches and evaluated dynamically across processes, **they cannot capture local variables from the stack**. 

The Roslyn static code analyzer (`Workflows.Analyzers`) will throw a build error if a lambda expression captures external local variables.

### The Problem (Analyzer Error)
```csharp
public async IAsyncEnumerable<Wait> Run(OrderState state)
{
    int targetId = 42; // Local variable on the stack
    
    yield return WaitSignal<OrderEvent>("OrderEvent", "WaitOrder")
        // ERROR: targetId is captured as a closure!
        .MatchIf((evt) => evt.OrderId == targetId); 
}
```

### The Solution: Explicit State Hand-off (`WithState`)
To use local variables inside your lambda expressions, pass them explicitly using `.WithState(...)`. This maps the local state into a serializable payload context that is safely passed to the delegate:

```csharp
public async IAsyncEnumerable<Wait> Run(OrderState state)
{
    int targetId = 42;
    
    yield return WaitSignal<OrderEvent>("OrderEvent", "WaitOrder")
        .WithState(targetId) // Explicitly hand off the variable
        .MatchIf((evt, id) => evt.OrderId == id); // Pass it to the lambda arguments
}
```

> [!NOTE]
> Referencing `this` pointer properties (e.g., `this.UserId`) inside lambdas is perfectly safe and allowed because `this` refers to the `WorkflowContainer` instance itself, which is already a stable reference fully serialized by the engine.
