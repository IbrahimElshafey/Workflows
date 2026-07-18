# Workflow Compensation Logic (Saga Pattern)

## 1. Why We Introduced Token-Based Compensation
Handling compensation manually via inline `if/catch` blocks forces developers to build procedural scripts riddled with temporary boolean flags to track partial failures. By leveraging a token-based `yield return Compensate(token)` approach, you shift the heavy lifting of state-tracking and result-caching to the engine.

This architecture solves several critical enterprise challenges:
* **Data-Driven Undoing:** To undo an action (e.g., refunding a payment), you need the data generated from the successful action (e.g., `TransactionId`). The engine automatically persists these results and injects them into the compensation delegates.
* **Code Locality:** `RegisterCompensation` forces developers to declare how to undo an action at the exact same moment they declare the action itself, preventing massive, decoupled `catch` blocks.
* **Forward vs. Backward Logic Split:** Standard execution (`OnResult`) is the "Forward Path" to advance the state machine. Compensation is the "Backward Path". Keeping them strictly separated allows the engine to persist "undo blueprints" for distributed sagas.

---

## 2. Real-World Scenarios and Code Samples

### A. Data-Driven Undoing (The Standard Saga)
In this scenario, a series of sequential commands act as a single logical transaction. The compensation delegate takes the `Result` of the command it is undoing.

```csharp
yield return ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
    "ProcessPayment", new ProcessPaymentCommand { OrderId = CurrentOrderId, Amount = 100 })
    .OnResult(async result => 
    {
        Console.WriteLine($"Payment processed: {result.TransactionId}"); 
    })
    // The engine automatically tracks this and injects the historical result if a rollback occurs
    .RegisterCompensation(async (result) => 
    {
        Console.WriteLine($"Refunding Transaction: {result.TransactionId}");
        await RefundPaymentAsync(result.TransactionId); 
    });
```

### B. Saga-within-a-Saga (Nested Scopes)
Tokens allow you to isolate rollbacks. If a branch in a `WaitGroup` fails, you can undo just that branch without touching the parent transaction. 

```csharp
// The command is tagged with both a Global and a Local token
yield return ExecuteCommand(new BookCarCommand { ... })
    .WithTokens("VacationSaga_123", "CarRentalSaga_123") 
    .RegisterCompensation(async (result) => { await CancelCarAsync(result.ReservationId); });

// Later in the workflow, if just the car rental branch fails:
yield return Compensate("CarRentalSaga_123"); 
// Only undoes the car rental. Flights and Hotels belonging to "VacationSaga_123" remain untouched.
```

### C. Waits and Sagas (Long-Running Pauses)
Unlike traditional sagas that fail on timeout due to in-memory locks, this engine can pause for weeks. 
```csharp
yield return WaitSignal("ManagerApproval"); // Engine serializes state and shuts down for 2 weeks
// ... 2 weeks later, manager rejects via webhook ...
yield return Compensate("OrderProcess"); // Background worker re-hydrates and executes compiled delegates as if no time passed
```

### D. The `goto` Statement for State Machine Replay
Because workflows compile to C#, you can use `goto` to cleanly rewrite history without drawing messy cyclic DAG graphs.
```csharp
PaymentStart:
yield return ExecuteCommand(new ChargeCardCommand()).WithTokens("RetryScope");

if (paymentFailed)
{
    yield return Compensate("RetryScope"); // Wipes the failed history cleanly
    goto PaymentStart; // Simply changes the <>1__state integer backward with zero overhead
}
```

---

## 3. Architecture: How the Background Compensation Worker Handles Rollbacks

Sagas and rollbacks are executed asynchronously and decoupled from the Runner's C# execution path. The system splits the lifecycle into:
1. **Runner (Compute Node):** Evaluates when a rollback is requested and yields a placeholder wait.
2. **Orchestrator (IO Node):** Enqueues and persists the rollback intent.
3. **Compensation Worker (Background Service):** Processes the saga rollback out-of-process.

### Phase 1: Compensation Registration (Forward Path)
1. **Wait Serialization:** As the Runner executes sequential commands (e.g., `ProcessPayment`), the yielded `CommandWait` contains compensation delegate references.
2. **DTO Generation:** The `CommandSerializer` maps the compensation metadata, tokens, and handler keys into the `CommandWaitDto`.
3. **Persistence:** The Orchestrator saves the DTOs into the SQL database. When a command completes successfully, its result payload and compensation tokens are persisted on the wait entity.

### Phase 2: Triggering the Rollback (The Yield)
1. **The Yield:** The C# state machine hits a failure path and yields `yield return Compensate("CarRentalSaga_123")`.
2. **Suspension:** The `CompensationWaitSerializer` interceptor creates a `CompensationWaitDto` for the token, updates the instance status to `InError` (or failed state), and suspends execution (`ContinueExecutionLoop = false`, `KeepInCache = false`).
3. **Persistence:** The Orchestrator commits the updated context, registers the compensation wait in the `CompensationWaits` index table, and notifies the channel.

### Phase 3: Out-of-Process Execution (Compensation Worker)
A background hosted service (`CompensationWorker.cs`) consumes the channel notifications and periodically sweeps the database for instances with status `InError` containing active `CompensationWaits`:
1. **LIFO Filtering:** The worker retrieves all completed command waits for that instance that match the target token. It reverses them to ensure **Last-In, First-Out** rollback ordering.
2. **Delegate Resolution:** Using `ICallbackRegistry` and the command DTO's `HandlerKey`, the worker loads the registered compensation handler and its delegate.
3. **Asynchronous Invocation:** The worker deserializes the historical command result payload and executes the compensation handler, passing the command result and the hydrated state object.
4. **Finalizing:** Once the stack is unwound, the worker marks the compensation wait as `Completed` and commits the updated state back to the database, resuming or finalizing the workflow execution.

---

## 4. Compensation Failure & Alerting Options

When an automated compensation action delegate throws an exception (e.g., refund API is down), the `CompensationWorker` catches the exception and stops processing the stack for that instance. The workflow instance remains suspended in **`InError`** status, and the `CompensationWaitDto` remains in **`Waiting`** status. 

To prevent instances from sitting silently in an ambiguous state, developers can implement alerting at two levels:

### A. Local Alerting (Inside the Workflow)
Because the engine uses compiler-generated C# state machines, developers can leverage standard C# language constructs and builder hooks directly in the `Run` method:
1.  **C# `try-catch` blocks:** Wrap saga stages inside a try/catch. If execution or compensation fails, catch the exception to execute inline compensation fallbacks or publish an alerting command to the service bus.
2.  **`.OnFailure()` callbacks:** Register an `.OnFailure(async err => ...)` hook on commands. If the forward execution fails or times out, the hook triggers local logging or immediate alert dispatch before yielding compensation.

### B. Global Alerting (Host App or Admin UI Config)
For centralized monitoring across all workflows, alerts can be configured at the platform level:
1.  **Global Admin Sweeper:** Implement a simple hosted background service that queries the RDBMS for instances in `InError` status where the `CompensationWaitDto.Status == Waiting` for longer than a specific threshold (e.g. 1 hour). The service raises alerts via Slack, PagerDuty, or email.
2.  **Admin UI Visuals:** The ASP.NET Core Admin MVC Dashboard aggregates these instances automatically, highlighting any instance in `InError` status with stuck active compensation wait points in the statistics and graph tree views.