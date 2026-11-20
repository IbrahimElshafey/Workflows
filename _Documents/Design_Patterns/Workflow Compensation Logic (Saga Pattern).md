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
4. **Finalizing:** Once the stack is unwound, the worker marks the compensation wait as `Completed` and commits the updated state back to the database. When *every* compensation wait on the instance is `Completed`, the instance is moved to **`Canceled`** - "rolled back cleanly": the forward business transaction never committed, so the instance is terminal, but it is **not** a failure and produces **no** dead-letter entry.
5. **Retry with backoff:** If a delegate throws, the worker records the attempt counters and the next eligible execution time *on the wait itself* (`CompensationWaitDto.Attempts`, `LastError`, `NextAttemptUtc`). The sweeper skips any wait whose `NextAttemptUtc` is still in the future, so retry state survives process restarts and retries are never executed faster than the configured backoff.
6. **Exhaustion:** When a wait exhausts `MaxAttempts`, the instance transitions to **`CompensationFailed`** and exactly one dead-letter entry is persisted and published (see section 4).

---

## 4. Compensation Failure & Alerting Options

### 4.1 Three Distinct Rollback Outcomes

A saga rollback can end in one of three states. They are deliberately different, because "we could not undo the work" is an operational incident while "we undid the work" is a clean (if unhappy) business outcome.

| Outcome | `WorkflowInstanceStatus` | `CompensationWaitDto.Status` | DLQ entry | Meaning |
| --- | --- | --- | --- | --- |
| Still rolling back / retrying | `InError` (300) | `Waiting` | No | At least one compensation wait is scheduled for a future attempt (see `NextAttemptUtc`). |
| Rolled back cleanly | `Canceled` (400) | `Completed` | No | Every compensation wait completed. The forward transaction never committed, so the instance is terminal but healthy. |
| Rollback failed / stopped | `CompensationFailed` (600) | `InError` | **Exactly one** | A compensation delegate exhausted its retry budget. The saga is left half-applied and needs an operator. |

`CompensationFailed` **never** means "rolled back" - it means the rollback itself failed or was abandoned. `Canceled` is the "fully rolled back" terminal state, consistent with the engine's existing semantics. An instance in `CompensationFailed` is parked: the periodic sweeper only scans `InError`, so it will not be retried automatically and cannot spam the DLQ.

### 4.2 Retry, Backoff and the Failure Path

When a compensation delegate throws (e.g. the refund API is down), `CompensationWorker` catches the exception per delegate - one faulty step does not abort the rest of the LIFO stack - and records the failure on the wait itself:

* `Attempts` - how many times this specific step has been executed;
* `LastError` - the last exception message, surfaced in the Admin UI;
* `NextAttemptUtc` - when the step becomes eligible again (`null` means immediately).

Because those three fields are part of `CompensationWaitDto`, they are serialized into the instance snapshot and mirrored into the `CompensationWaits` index table, so retry state survives process restarts and app-pool recycles. Snapshots written before this feature existed deserialize with `Attempts = 0` / `NextAttemptUtc = null`, so upgrades are non-breaking.

A **missing compensation handler** is treated exactly like a throwing one. `ICallbackRegistry` is populated in-process when the forward command wait is mapped, so the entries do not survive a restart, and a handler can also be removed by a later deployment. If a completed command carries the saga token but no `{HandlerKey}:Compensation` callback (or no invoker) can be resolved, the worker does not silently skip it: it throws `No compensation handler is registered for command handler(s): ...`, which counts as a failed attempt. The wait therefore retries with backoff and, once `MaxAttempts` is exhausted, the instance lands in `CompensationFailed` with a DLQ row instead of being reported as a cleanly rolled-back `Canceled` instance. Register the compensation callbacks before the sweeper runs, or accept that the rollback needs an operator.

The sweeper skips any wait whose `NextAttemptUtc` is still in the future. Delays are exponential:

```
delay = min(InitialDelay * BackoffMultiplier^(Attempts - 1), MaxDelay)
```

`CompensationWorkerOptions` controls the policy and is registered with `TryAddSingleton`, so a host application can override it:

| Option | Default | Description |
| --- | --- | --- |
| `MaxAttempts` | `3` | Attempts per compensation delegate before the instance moves to `CompensationFailed`. |
| `InitialDelay` | `1s` | Delay before the second attempt. |
| `BackoffMultiplier` | `2` | Growth factor applied to each subsequent delay. |
| `MaxDelay` | `30s` | Upper bound for a single backoff window. |
| `SweepInterval` | `1s` | How often the worker sweeps the database for `InError` instances with waiting compensations. |

```csharp
// Register your overrides BEFORE AddWorkflowsInProcessHost (it uses TryAddSingleton).
services.AddSingleton(new CompensationWorkerOptions
{
    MaxAttempts = 5,
    InitialDelay = TimeSpan.FromSeconds(5),
    BackoffMultiplier = 3,
    MaxDelay = TimeSpan.FromMinutes(2),
    SweepInterval = TimeSpan.FromSeconds(5)
});
```

### 4.3 Dead Letter Queue

When a compensation delegate exhausts `MaxAttempts`, the worker performs two actions in order:

1. **Persist (atomically):** transition the instance to `CompensationFailed`, store the aggregated failure message on the instance (`WorkflowStateDto.ErrorMessage`), and append a row to the `DlqMessages` table. Both writes go through a single `IWorkflowStore.SaveContextSyncAsync(state, completedWaitIds, triggeringSignalId: null, dlqMessage: message)` call, so they are covered by the same database transaction: either the instance becomes `CompensationFailed` **and** the DLQ row exists, or neither happens. The row records the instance id, workflow type, failure count, error message and `FailedAt`. Because the instance is parked as `CompensationFailed` (and the sweeper only scans `InError`), a restarted worker cannot create a second row.
2. **Publish:** hand the same payload to `IDlqPublisher.PublishAsync` for downstream alerting (Service Bus, SQS, Slack bridge, ...). Publisher exceptions are logged and never rethrown - a broken alerting transport must not block the rollback state machine.

**Delivery window.** Persisting and publishing are deliberately *not* atomic with each other. The transaction commits first, then the publisher is invoked, which gives an at-least-once, not exactly-once, guarantee in one direction only:

| Crash point | Outcome |
| --- | --- |
| Before the transaction commits | Neither the status change nor the DLQ row exist. The instance is still `InError`, so the sweeper retries the rollback normally. |
| After commit, before/while publishing | The DLQ row exists and the instance is parked, but subscribers never saw the message. The `DlqMessages` table is the source of truth: reconciliation jobs, the Admin UI, or a re-publish over the stored row close the gap. |
| After publishing succeeds | Fully delivered. |

So subscribers must treat the DLQ as *at-least-once from the database boundary*: read `DlqMessages` for the authoritative list, and de-duplicate on `(WorkflowInstanceId, CreatedAt)` if both the row-based and publisher-based paths feed the same sink. `AppendDlqMessageAsync` still exists for callers that only want the row (for example an ops backfill), but the compensation worker no longer uses it - the atomic overload is the supported rollback path.

`IDlqPublisher` is registered as a no-op (`NoOpDlqPublisher`) by default, so `CompensationWorker` always resolves a publisher; register your own implementation earlier in the container to win over the default.

```csharp
public sealed class ServiceBusDlqPublisher : IDlqPublisher
{
    public Task PublishAsync(DlqMessage message, CancellationToken ct = default) =>
        _sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.Serialize(message)), ct);
}
```

### 4.4 Admin UI: "Stuck Rollbacks" and Manual Retry

* **"Stuck Rollbacks" filter** on the Instances index lists every instance in `CompensationFailed`, rendered with a red badge and a warning row, so half-applied sagas are impossible to miss.
* **"Retry Compensation"** on the instance detail page is available while `EnableWriteActions` (and `CanExecuteActions`) allows it. It calls `IAdminCommandService.RetryCompensationAsync(instanceId, ct)`, which:
  1. resets every compensation wait of the instance to `Waiting` with `Attempts = 0`, `LastError = null` and `NextAttemptUtc = null`;
  2. moves the instance from `CompensationFailed` back to `InError` and clears `ErrorMessage`/`CompletedAt`;
  3. re-queues the rollback through the compensation channel so the worker picks it up immediately.

The command is idempotent: it only acts on instances currently in `CompensationFailed`, so a double-click or a repeated POST is a no-op and can never produce a second DLQ entry. Fix the underlying dependency (payment provider, shipping API, ...) before retrying.

### 4.5 Inline Alerting from Inside the Workflow (`OnFailure`)

`OnFailure` is the *inline* alert path. It runs in the runner's execution context the moment a forward command fails or times out, before the saga is ever escalated to the background worker, which makes it the right place for low-latency, per-step notification.

```csharp
yield return ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
        "ProcessPayment", new ProcessPaymentCommand { OrderId = CurrentOrderId, Amount = 100 })
    .OnFailure(async err =>
    {
        // Inline alert: fires as soon as this step fails, before any compensation runs.
        _logger.LogError(err, "Payment failed for order {OrderId}", CurrentOrderId);
        await _alerts.PublishAsync($"Payment failed for order {CurrentOrderId}: {err.Message}");
    })
    .OnResult(async result =>
    {
        Console.WriteLine($"Payment processed: {result.TransactionId}");
    })
    .RegisterCompensation(async result =>
    {
        // Backward path: only runs if this step had already committed.
        await RefundPaymentAsync(result.TransactionId);
    });
```

`OnFailure` versus `CompensationFailed`: `OnFailure` alerts on a failed *forward* step (nothing has been committed yet, or the engine is about to undo it), while `CompensationFailed` reports a failed *backward* step (the undo itself could not be applied, leaving the saga half-applied). Wire `OnFailure` to fast, per-step alerting and `IDlqPublisher` to the operational dead-letter queue.

### 4.6 Global Monitoring (Host App or Admin UI Config)

1.  **Global Admin Sweeper:** a hosted background service can query the RDBMS for instances in `InError` whose compensation wait is `Waiting` and has made no progress, or for `CompensationFailed` instances whose `LastError` has not changed for longer than a threshold (e.g. 1 hour), and raise Slack/PagerDuty/email alerts. For `CompensationFailed`, prefer the DLQ (`DlqMessages`) - one row per failed rollback, no duplicate scanning.
2.  **Admin UI Visuals:** the ASP.NET Core Admin MVC dashboard highlights `CompensationFailed` instances with a red `CompensationFailed` badge, a warning row, and the "Stuck Rollbacks" filter described above, while `InError` instances with stuck active compensation waits still surface in the statistics and graph views.