
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

---

## 6. Checklist: The "Final 1%"

Before moving to production, these items must be confirmed in the `WorkflowRunner` logic:

* [ ] **Optimistic Concurrency:** Does the `StateMachineObject` have an `ETag` or `RowVersion` to prevent two servers from advancing the same workflow simultaneously?
* [ ] **Payload Monitoring:** Is there a guardrail/log if the `Variables` dictionary exceeds a specific size (e.g., 1MB)?
* [ ] **Zombie Recovery:** Is there a "Suspended" status for workflows that fail `X` times, preventing infinite crash loops?