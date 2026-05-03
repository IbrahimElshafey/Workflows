# 🏗️ Workflows Engine: Architectural Reference Guide

## Core Philosophy
The Workflows engine is built on a strict separation between **Domain Definition**, **I/O & Persistence**, and **Compute & Execution**. By leveraging the C# compiler's native state machine (`IAsyncEnumerable`), the system provides a highly scalable, snapshot-based workflow engine without the overhead of event-sourcing/replay mechanisms.

---

### 1. Workflows.Definition (The DSL Layer)
**Responsibility:** Provide a pure, zero-dependency Domain Specific Language (DSL) for developers to author workflows.

* **Zero Dependencies:** This library has no knowledge of databases, messaging queues, or execution contexts. It is purely standard C#.
* **The State Machine:** Workflows are defined by inheriting from `WorkflowContainer` and using native C# `IAsyncEnumerable<Wait>` with `yield return` statements for control flow.
* **Wait Primitives:** Provides the core suspension instructions: `WaitSignal`, `WaitUntil` (Time), `WaitGroup` (MatchAny/MatchAll), and `WaitSubWorkflow`.
* **Clean Abstraction:** Infrastructure-specific Data Transfer Objects (DTOs) like `WaitDto` or `WorkflowRunContext` are kept `internal`. The library uses the `[InternalsVisibleTo("Workflows.Runner")]` pattern. This ensures the workflow author's IntelliSense remains perfectly clean while still allowing the engine to extract necessary structural data.

---

### 2. Workflows.Orchestrator (The IO & Routing Layer)
**Responsibility:** Act as the "Traffic Cop." It handles incoming webhooks/signals, fast database queries, and payload routing. It is deliberately "dumb" regarding C# internals.

* **Partial Matching:** When a signal arrives, the Orchestrator does *not* evaluate C# expressions. It performs a fast, relational database lookup (Partial Match) against flat, mandatory indexing keys (`ExactMatchPart` strings) extracted during the workflow's previous step.
* **Storage Split Strategy:** * **SQL (Relational):** Used for querying flat data like Registration, Signals, and Wait indexes.
    * **NoSQL/Document (JSON):** Used for storing the `WorkflowRunContext` (serialized closures, local variables, and the state machine index).
* **Transaction Coordinator:** It is the sole owner of ACID transactions. When the Runner returns a result, the Orchestrator atomically updates the `WorkflowRunContext`, deletes the old waits, and inserts the new waits.
* **State Hydration:** Once a partial match is found, the Orchestrator fetches the raw `WorkflowRunContext` and sends it over the communication bus to the Runner.

---

### 3. Workflows.Runner (The Compute & Execution Layer)
**Responsibility:** Act as the "Brain." It handles state machine manipulation, C# expression tree parsing, delegate compilation, and exact matching. **It is 100% stateless and never touches the database.**

* **The Template Cache:** Memory-caches previously seen workflow step structures (`MatchExpressionTemplate`). This eliminates the massive performance penalty of parsing and compiling C# Expression Trees (`Reflection.Emit`) on every execution.
* **Exact Matching:** If a cache miss occurs, it deserializes the expression, extracts routing keys, and compiles a `CompiledMatchDelegate`. It executes this delegate in memory to guarantee an incoming signal genuinely satisfies the workflow's logic.
* **State Machine Execution:** 1. Injects the signal payload into the workflow's closure (`AfterMatchAction`).
    2. Hydrates the C# compiler-generated state machine using `ICoreRunner` (reflection-free).
    3. Advances the `IAsyncEnumerable` to the next `yield return`.
* **DTO Generation:** It parses any newly yielded waits, extracts the flat mandatory DB keys, maps everything into a `WaitDto`, serializes the new state context, and returns the payload to the Orchestrator.

---

### 4. Communication Abstraction (The Decoupling Layer)
**Responsibility:** Ensure the Orchestrator and Runner are physically and logically decoupled, allowing the system to scale from a single-process monolith to a distributed microservice architecture.

* **Transport Agnostic:** Communication is defined by interfaces (e.g., `IWorkflowRunnerClient`, `IWorkflowRunResultSender`), meaning the underlying transport can be swapped between direct in-memory calls, RabbitMQ, Azure Service Bus, or Kafka.
* **The Flow:**
    1.  **Signal Arrival:** Orchestrator receives an external event.
    2.  **Dispatch to Compute:** Orchestrator places a `RunWorkflowCommand` (containing the Signal Payload and `WorkflowRunContext`) onto the bus.
    3.  **Processing:** A Runner node picks up the message, evaluates the exact match, advances the state machine, and generates the new state.
    4.  **Result Return:** The Runner places a `WorkflowRunResult` (containing the updated Context and new `WaitDto`s) back onto the bus.
    5.  **Persistence:** The Orchestrator receives the result and commits the changes to the database in a single transaction.
* **Cancellation & Commands:** Follows a Command pattern where workflows can yield active intents (like a `CancelTokenCommand`). The Runner evaluates these locally to fast-forward state (skipping cancelled branches) without requiring database round-trips until a new passive wait is reached.