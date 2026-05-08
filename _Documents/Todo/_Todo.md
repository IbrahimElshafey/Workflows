This is the definitive blueprint for the **Workflows 2.0 Core Architecture**. We have moved from "hacking the compiler" to an explicit, data-driven, and high-performance system.

---

## 1. Explicit State Pattern (The Closure Killer)

To eliminate the volatility of compiler-generated classes (`<>c__DisplayClass`), we enforce an explicit state hand-off.

* **The Authoring Rule:** Developers must use `.WithState(T)`. This ensures the lambda is a cached function pointer rather than an object instance capturing local RAM.
* **The Structure:** The `Wait` object owns a property `public object ExplicitState { get; internal set; }`.
* The `MatchIf` signature becomes `Func<TSource, TState, bool>`.


* **The Benefit:** Zero reliance on hidden compiler classes. The state is now just "data" attached to the `Wait` record.

---

## 2. State Machine Serialization (Duck-Typing Bridge)

We treat the state machine not as a black-box object, but as a **Property Bag**. This ensures that if you add a line of code, the workflow doesn't break.

* **Dehydration:** The engine scans the state machine fields for "lifted locals" (e.g., `<count>5__1`). It strips the compiler junk and stores `{"count": 10}` in a `Dictionary<string, object>`.
* **Hydration:** When resuming, the engine looks at the *current* state machine. It doesn't care if the field is now named `<count>5__2`. It finds the field semantically matching "count" and injects the value `10`.
* **Snapshotting:** We save the entire `Instance` (WorkflowContainer) and the `Variables` (Locals) as a JSON snapshot, allowing for instant resumption without replaying history.

---

## 3. The Roslyn Analyzer (The Ironclad Guard)

The analyzer is packaged in the `analyzers/dotnet/cs` folder of your NuGet to enforce rules at compile-time.

| Error Code | Rule | Reason |
| --- | --- | --- |
| **WF001** | **No Implicit Closures** | Prevents capturing variables without `.WithState()`. |
| **WF002** | **Sealed Workflows Only** | **(New)** Workflows must be `sealed`. Prevents fragile inheritance chains and allows JIT devirtualization. |
| **WF003** | **No Anonymous Types** | Forces `ValueTuple` or `record` for state to ensure serialization stability. |
| **WF004** | **No Unserializable Locals** | Blocks `IDisposable`, `Stream`, or `SqlConnection` from being declared as local variables. |

---

## 4. Newtonsoft Settings for Performance

To prevent serialization from becoming the bottleneck, we move away from standard reflection and minimize GC pressure.

* **Contract Caching:** Reuse a single static `JsonSerializerSettings` instance. This caches the **IL-emitted** delegates so the second serialization call is near-native speed.
* **Array Pooling:** Use `IArrayPool<char>` (via `System.Buffers.ArrayPool`) to "rent" buffers, preventing GC Gen 0 thrashing under heavy load.
* **Reference Preservation:** Set `PreserveReferencesHandling.Objects` to maintain memory integrity if multiple waits share the same state object.
* **Streaming:** Serialize directly to the `Stream` (DB/Blob) rather than creating large intermediate `string` objects.

---

## 5. Beating the Competitors (Fan-Out & Observability)

We incorporate the best ideas from Temporal and Durable Functions while keeping our C#-centric simplicity.

### Massive Fan-Out (The "External State" Pattern)

* **The Problem:** Storing 10,000 wait-results in a single JSON snapshot causes "State Bloat."
* **The Fix:** Introduce a `WaitMany` or `WaitAny` that doesn't hold data in the snapshot. Instead, it points to a "Wait Correlation ID." The signals are counted in a high-performance SQL table. The workflow only hydrates when the "Count Reached" event triggers, keeping the workflow JSON tiny.

### Visual Observability (The Hybrid Audit Log)

* **The Problem:** Code-only workflows are "black boxes" for business users.
* **The Fix:** Implement a **Command & Signal History**. Even though we resume from a Snapshot (fast), we record every signal received and command sent in a separate audit table. This allows the UI to render a "Step-by-Step" timeline for any instance.

---

## 6. Checklist: The "Final 1%"

Before moving to production, these items must be confirmed in the `WorkflowRunner` logic:

* [ ] **Optimistic Concurrency:** Does the `StateMachineObject` have an `ETag` or `RowVersion` to prevent two servers from advancing the same workflow simultaneously?
* [ ] **DI Scoping:** Does the Runner create an `IServiceScope` for *each* execution turn and dispose of it immediately after yielding?
* [ ] **Payload Monitoring:** Is there a guardrail/log if the `Variables` dictionary exceeds a specific size (e.g., 1MB)?
* [ ] **The "this" Pointer:** Does the Hydrator correctly assign the `Instance` to the `<>4__this` field of the state machine before execution?
* [ ] **Zombie Recovery:** Is there a "Suspended" status for workflows that fail `X` times, preventing infinite crash loops?

**This architecture is now a "Masterpiece" of .NET engineering.** It is fast, version-immune, and developer-friendly.