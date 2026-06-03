# High-Performance Workflows Engine: Commands System Specifications

## 1. Architectural Philosophy & Purpose

The Workflows Engine enforces a strict separation between **Compute Logic (The Stateless Runner)** and **I/O, Persistence, and Routing (The Orchestrator)**.

To maintain 100% infrastructure agnosticism within the domain logic layer, workflows are completely forbidden from executing side effects directly (e.g., querying external databases, making direct HTTP calls, or publishing directly to raw message brokers). Instead, workflows are purely logical state-machines that run inside a stateless compute loop, changing memory fields and yielding **Intents** back to the framework infrastructure.

The **Commands System** provides the foundational execution framework for these intents. Primitives are categorized into two execution modes to gracefully handle the physics of time, distributed networking boundaries, and state consistency:

* **Synchronous Commands:** Fast, blocking request-reply execution threads managed entirely inside the current Runner tick without pausing or dehydrating the C# state machine.
* **Asynchronous Commands:** Scalable, non-blocking distributed transactions that pause the workflow execution, dehydrate its state to the database aggregate root, and gracefully handle long-lived external callbacks and webhooks natively without blocking compute threads.

---

## 2. Primitives Blueprint & The Workflow DSL

The workflow abstract container supports both command types natively. The workflow code remains clean C# business logic; all underlying routing, transport envelope management, and correlation matching happen strictly out-of-process.

### The Authoring Interface

```csharp
public sealed class OrderFulfillmentWorkflow : WorkflowContainer
{
    private string _orderId;
    private decimal _amount;
    private string _trackingId;

    public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync()
    {
        // 1. SYNCHRONOUS COMMAND (Blocking request-reply local/remote call)
        var inventoryCheck = yield return ExecuteSynchronousCommand<CheckStockCommand, StockResult>(
            "InventoryService.CheckStock",
            new CheckStockCommand { OrderId = _orderId })
            .WithRetries(maxAttempts: 2, backoff: TimeSpan.FromSeconds(2));

        if (!inventoryCheck.IsAvailable)
        {
            this.Status = "OutOfStock";
            yield break;
        }

        // 2. ASYNCHRONOUS COMMAND (Out-of-process distributed transaction)
        yield return ExecuteAsynchronousCommand<ChargePaymentCommand, PaymentResult>(
            "PaymentService.ChargeCard",
            new ChargePaymentCommand { OrderId = _orderId, Amount = _amount })
            .OnResult(result => { this._trackingId = result.StripeTransactionId; })
            .RegisterCompensation("BillingScope", () => new RefundPaymentCommand(_trackingId));

        // The workflow is frozen here by the engine. 
        // Execution resumes natively on the next line when the Orchestrator routes the result.
        
        this.Status = "FulfillmentCompleted";
    }
}

```

---

## 3. Asynchronous Commands System

### Definition & Purpose

Asynchronous Commands are used for distributed, out-of-process tasks that require significant time to complete, rely on fragile third-party networks (e.g., Stripe, PayPal, partner shipping systems), or communicate via asynchronous messaging patterns (webhooks/queues). They prevent web server thread exhaustion by letting the thread die the moment an operation crosses the network boundary.

### Architectural Lifecycle Flow

1. **Intent Yielding:** The workflow yields an `AsynchronousCommand` object containing the payload data contract.
2. **Compute Suspension:** The `WorkflowRunner` instantly stops executing the C# state machine. It does **not** make a network call. It wraps the command payload with instance state metadata and returns control to the `Orchestrator`.
3. **Transactional Commit (Outbox):** The `Orchestrator` receives the context. It opens a single database ACID transaction to do three things atomically:
* Update the `WorkflowInstances` state snapshot JSON blob (`StateObject`).
* Save a tracking index row to the `CommandWaits` relational index table with `Status = Waiting`.
* Insert the command DTO string into a database `OutboxMessages` table.


4. **Decoupled Dispatch:** A background relay service tails the Outbox table and passes the message to the registered `IPublisher`. The message leaves the building. The Runner thread is entirely dead and resources are freed.
5. **Agnostic Processing:** The external microservice processes the payload completely independently. It contains zero references to the workflow engine DLLs. Once completed, it broadcasts a response payload or domain event via the transport broker.
6. **Orchestrator Ingestion & Matching:** The `ISubscriber` intercepts the response message. The `Orchestrator` runs its configured **Correlation Matching Expression** to instantly extract the target key. It runs a Tier 1 SQL index scan against the `CommandWaits` table, matches the instance, pulls the `WorkflowRunContext`, and routes the payload to a stateless `WorkflowRunner` thread pool worker to advance the code machine to the next line.

### Split-Layer Registration Pass

Because Asynchronous Commands involve decentralized, multi-application network loops, they must be registered on both the Runner cluster and the core Orchestrator gateway.

#### A. Runner Registration (Pure Identity)

To keep the execution compute unit pristine and lightweight, the Runner only needs to map the command type to a unique string identifier.

```csharp
// Runner Initialization Pass
services.RegisterWorkflowCommands(registry => 
{
    registry.RegisterAsynchronousCommand<ChargePaymentCommand>("PaymentService.ChargeCard");
});

```

#### B. Orchestrator Registration (Infrastructure Topology)

The Orchestrator owns the I/O plumbing. It requires an explicit mapping detailing how to emit the payload, where to listen for the response, and how to link the returning payload to the sleeping database row.

```csharp
// Orchestrator Startup Layer (Program.cs)
services.AddWorkflowOrchestrator(options =>
{
    options.RegisterAsynchronousCommandRoute<ChargePaymentCommand, PaymentResultResponse>(
        identifier: "PaymentService.ChargeCard",
        publisher: provider => provider.GetRequiredService<IRabbitMqPublisher>(),
        subscriber: provider => provider.GetRequiredService<IStripeWebhookSubscriber>(),
        
        // High-Performance Correlation: Translates to a direct Tier-1 Database Index
        correlationMatch: (commandSent, incomingResponse) => 
            commandSent.OrderId == incomingResponse.Payload.Metadata.OrderId
    );
});

```

---

## 4. Synchronous Commands System

### Definition & Purpose

Synchronous Commands are designed for request-response execution boundaries that resolve quickly or are natively handled inside the immediate application boundary. They allow the workflow author to maintain strict encapsulation and leverage dependency injection formatting without paying the database persistence or network outbox overhead tax.

### Architectural Lifecycle Flow

1. **Local Evaluation:** The workflow encounters `ExecuteSynchronousCommand`.
2. **Compute Thread Continuity:** The `WorkflowRunner` loop does **not** pause or dehydrate the context to the SQL database. It keeps the current thread alive and active.
3. **Handler Delegation:** The Runner leverages a pre-compiled, zero-reflection delegate from the `CommandTemplateCacheRecord` to talk to an internal `ICommandHandlerFactory`.
4. **Capability Invocation:** The factory uses standard .NET Dependency Injection to resolve the concrete `ICommandHandler<TCommand, TResult>` registered inside the app container.
5. **Synchronous Resolution:** The handler acts as the explicit I/O gateway. It executes the work (e.g., calling a fast Redis cache, executing an internal algorithm, wrapping an HTTP client request), blocks the thread until it receives the response, and returns the concrete output object.
6. **Immediate Advancement:** The `WorkflowRunner` captures the result, mutates the local memory fields of the active `WorkflowContainer`, and instantly executes `MoveNextAsync()` to step to the next line of C# in the exact same compute cycle.

### Runner-Only Registration Pass

Because Synchronous Commands never break the compute thread loop or store tracking keys in the database tables, the **Orchestrator is completely blind to them**. No registration is needed or allowed on the Orchestrator side. They are configured exclusively inside the Runner application cluster.

```csharp
// Runner Configuration Pass (Program.cs)
services.RegisterWorkflowCommands(registry =>
{
    // The developer passes the explicit capability execution handler directly
    registry.RegisterSynchronousCommand<CheckStockCommand, StockResult>(
        identifier: "InventoryService.CheckStock",
        handler: provider => provider.GetRequiredService<CheckStockCommandHandler>()
    );
});

```

---

## 5. Architectural Comparison Matrix

| Core Architectural Metric | Synchronous Commands | Asynchronous Commands |
| --- | --- | --- |
| **Execution Path** | Blocking Request-Reply | Non-Blocking Distributed Cycle |
| **Runner Thread Behavior** | Stays alive; keeps spinning loop tick | Dies immediately; thread is released |
| **State Persistence Tax** | None ($0 Database writes) | Snapshot-dehydration to Document DB |
| **Orchestrator Awareness** | 100% Blind (No registration required) | Active Manager (Enforces correlation) |
| **Routing Strategy** | Direct DI Handler Factory resolution | Relational `CommandWaits` Key Lookup |
| **Failure Recovery** | Local try-catch / Short retry intervals | Transactional Outbox + Saga Compensation Stack |
| **Optimal Use Case** | Fast internal lookups, RAM math, logging | External Webhooks, Stripe payments, multi-day tasks |

---

## 6. Failure Modes & Reliability Guarantees

### Dual-Write Prevention in Asynchronous Commands

A recurring trap in distributed systems is publishing a message to a queue while trying to write state to a database. If the queue is hit first and the database write fails, the command is executed but the engine rolls back, causing duplicate execution on retry.

To maintain a zero-data-loss guarantee, this engine eliminates direct publishing. The Orchestrator forces all outbox updates into the exact same database-scoped transaction as the workflow context snapshot:

```csharp
using var transaction = await _dbContext.Database.BeginTransactionAsync();
try
{
    // 1. Snapshot the state machine
    await _instanceStore.SaveStateAsync(instanceContext.Id, instanceContext.JsonBlob);
    
    // 2. Set up database correlation index
    await _waitStore.InsertCommandWaitIndexAsync(instanceContext.Id, commandKey);
    
    // 3. Write intent data contract to outbox table
    await _outboxStore.EnqueueAsync(new OutboxItem { Payload = commandDto });
    
    // Atomic commit to single RDBMS
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync(); // Nothing leaves the system
    throw;
}

```

An independent, out-of-process background worker loops continuously to drain the `OutboxMessages` database table and hand the verified payload to the `IPublisher` safely.

### The Saga Compensation Stack

When a downstream asynchronous command faults permanently or an SLA timeout breaches, the engine executes the **Backward Unwinding Path**.

The Runner does not run expensive SQL queries to trace execution maps across microservices. Instead, it reads the agnostic **Wait Tree** document that was hydrated into RAM when the instance woke up.

It reads the completed blocks marked under the target scope token, loops through them in strict reverse-chronological (LIFO) order, hydrates their cached compensation factories with the historic payload results saved directly inside the JSON node, and executes the rollback commands reliably.