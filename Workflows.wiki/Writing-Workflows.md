# Writing Workflows

Workflows are defined as C# classes that inherit from `WorkflowContainer` and implement a run method returning `IAsyncEnumerable<Wait>`.

---

## 1. Declaring a Workflow

To author a workflow:
1. Decorate the class with the `[Workflow]` attribute to specify its unique name and version.
2. Declare the class as `sealed` (enforced by the `Workflows.Analyzers` Roslyn analyzer).
3. Add serializable properties on the class to represent your persistent domain state.

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

## 3. Roslyn Static Analysis Rules (`Workflows.Analyzers`)

Workflow container classes are checked at compilation time by Roslyn analyzers to guarantee execution safety across process boundaries:

* **Unsealed Container Check**: All classes inheriting `WorkflowContainer` must be declared `sealed`.
* **Closure Capture Prevention**: Lambda matchers (`MatchIf`, `AfterMatch`) are forbidden from capturing stack-local variables.
* **WF300 Version Drift Warning**: Triggered when a `[Workflow("Name", version)]` version is incremented, offering an IDE quick-fix to archive the old version layout and extract contract schemas.

---

## 4. Strict No-Closure Enforcement (Important)

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
Only genuine stack values — locals and method parameters — need to be handed off. `.WithState(...)` serializes the value into the wait payload so it survives the process boundary and is injected as an extra lambda argument:

```csharp
public async IAsyncEnumerable<Wait> Run(OrderState state)
{
    int targetId = state.TargetId; // parameter -> local: the parameter itself cannot be captured
    
    yield return WaitSignal<OrderEvent>("OrderEvent", "WaitOrder")
        .WithState(targetId) // Explicitly hand off the local
        .MatchIf((evt, id) => evt.OrderId == id); // Pass it to the lambda arguments
}
```

### What NOT to pass to `WithState`
Container properties (`this.UserId`, `this.CurrentOrderId`) and the workflow input POCO (`state`) must **not** be wrapped in `WithState`:

```csharp
// ✗ WRONG - both calls are pointless and change the semantics
yield return WaitSignal<OrderEvent>("OrderEvent", "WaitOrder")
    .WithState(this.UserId)   // freezes a snapshot of a live property
    .WithState(state)         // copies the whole input POCO into the wait payload
    .MatchIf((evt, s) => evt.OrderId == s.OrderId)   // compares against the frozen copy
    .AfterMatch((evt, s) => UserId = s.UserId);      // stateful overload, also the frozen copy

// ✓ CORRECT - instance members are rebound to the live deserialized container
yield return WaitSignal<OrderEvent>("OrderEvent", "WaitOrder")
    .MatchIf((evt) => evt.OrderId == UserId)
    .AfterMatch((evt) => UserId = evt.UserId);
```

Why the "wrong" version is harmful:

1. **No closure is avoided.** A property access inside a matcher compiles to a `this` reference, and the engine rewrites that reference to the live `WorkflowContainer` instance when the matcher is materialized. The analyzer (`WF000`) only rejects captures of *locals and parameters*.
2. **The value is frozen.** `WithState` snapshots the value when the `Wait` is created, so a property assigned in an earlier step is not reflected in later comparisons.
3. **The snapshot is persisted twice.** Each explicit state payload is written into machine-state `Locals` (keyed by the wait id *and* by the state key) on every suspend, bloating the serialized workflow instance.
4. **It drags the whole chain into the stateful overloads** (`MatchIf((sig, st) => ...)`, `AfterMatch((sig, st) => ...)`, `OnCanceled(st => ...)`), coupling your callbacks to a snapshot instead of the live container.

> [!NOTE]
> Referencing `this` pointer properties (e.g., `this.UserId`) inside lambdas is perfectly safe and allowed because `this` refers to the `WorkflowContainer` instance itself, which is already a stable reference fully serialized by the engine. For the input POCO, copy what you need into container properties (or locals) once, then read those from the matchers.

> [!TIP]
> The analyzer catches both mistakes at compile time: **WF008** warns when a workflow container member (property/field, or `this` itself) is passed to `.WithState(...)`, and **WF009** warns when the input state parameter of a `Run`/sub-workflow method is passed instead of a copy. Parameters of ordinary helper methods are *not* flagged — those values are not stored in the machine state, so the hand-off is genuinely required there.

### Anonymous Types Are Not Accepted

`WithState` rejects anonymous objects. `new { OrderId = 1 }` is a compiler-generated type with no constructor a deserializer can bind to, so the value could never be restored after a resume. The analyzer reports it as an error (`WF003`) and `WithState` additionally throws `ArgumentException` at wait-creation time, so the mistake cannot slip past into production:

```csharp
// ✗ REJECTED - WF003 compile error, ArgumentException at runtime
yield return WaitSignal<OrderEvent>("OrderEvent", "WaitOrder")
    .WithState(new { OrderId = 1 });

// ✓ Use a named class or record instead - it survives the round-trip
yield return WaitSignal<OrderEvent>("OrderEvent", "WaitOrder")
    .WithState(new OrderWaitState { OrderId = 1 });

public sealed class OrderWaitState { public int OrderId { get; set; } }
```

---

## 5. CLI Tooling & Schema Verification (`dotnet-wf`)

Once workflows are defined, use the `dotnet-wf` CLI tool to export contracts and verify breaking changes:

```bash
# Export versioned contract schema JSON
dotnet wf schema --assembly "./bin/Release/net10.0/OrderWorkflows.dll" --out "./_Schemas"

# Verify compatibility against prior assembly versions in CI/CD pipeline
dotnet wf verify --old "./bin/V1/OrderWorkflows.dll" --new "./bin/V2/OrderWorkflows.dll"
```