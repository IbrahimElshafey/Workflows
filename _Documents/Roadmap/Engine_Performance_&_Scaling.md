# Engine Performance & Scaling: Optimizations and Architecture

This document describes planned performance optimizations, concurrency safeguards, and scaling architectural features for the Workflows engine.

## 1. High-Performance JSON Serialization (Newtonsoft.Json Settings)

To prevent serialization/deserialization of workflow contexts from becoming the CPU and memory bottleneck, we minimize Garbage Collector (GC) pressure and avoid reflection in serialization loops.

* **Contract Caching**: Reuse a single static `JsonSerializerSettings` instance. This caches IL-emitted serialization delegates, ensuring subsequent serialization/deserialization calls run at near-native speeds.
* **Array Pooling**: Use `IArrayPool<char>` (via `System.Buffers.ArrayPool`) to rent buffers for reading and writing JSON data, eliminating GC Gen 0 thrashing under heavy workflow execution loads.
* **Reference Preservation**: Configure `PreserveReferencesHandling.Objects` to maintain object identity reference loops and graph structure when multiple waits or callbacks share references to the same state entity.
* **Direct Streaming**: Serialize directly to and from IO streams (`Stream` from DB/Blob storage) instead of allocating large intermediate `string` buffers in memory.

---

## 2. Massive Fan-Out (The "External State" Pattern)

* **The Problem**: Yielding composite waits like `WaitGroup` with 10,000+ children (e.g., waiting for thousands of parallel microservice responses) causes **State Bloat** because the entire wait-tree and results are serialized inside a single JSON context snapshot.
* **The Fix**: Introduce `WaitMany` or `WaitAny` waits that store state out-of-process. Instead of carrying child wait structures inside the C# state machine JSON:
  1. The wait points to a **Wait Correlation ID** in a dedicated, high-performance database table.
  2. Signals are counted and aggregated relationally.
  3. The workflow engine is only woken up when a SQL trigger or count-completed event fires.
  4. This keeps the active workflow JSON context tiny, enabling massive scale fan-out patterns.

---

## 3. Concurrency Safeguards & Guardrails

Prior to final production deployment, the following engine behaviors must be validated or implemented:

* **Optimistic Concurrency**: Ensure the database records (in `WorkflowInstances`) contain a `RowVersion` or `ETag` to prevent two concurrent Runner nodes from picking up and advancing the same workflow instance simultaneously (preventing state overwriting).
* **Payload Monitoring & Size Guardrails**: Implement logs or circuit breakers that trigger if the `ExplicitState` or local variable payload exceeds a specific threshold (e.g., 1MB) to prevent memory allocation spikes.
* **Zombie Recovery**: Implement a retry policy limit with a `Suspended` status for workflows that fail repeatedly during state machine execution (e.g., throwing repeated execution exceptions) to prevent infinite engine crash loops.
