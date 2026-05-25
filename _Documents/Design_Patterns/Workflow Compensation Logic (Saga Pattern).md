My apologies! In my haste to correct the architectural flow, I left out the code samples and real-world scenarios we had built. 

Here is the **complete, finalized documentation**, combining the rich code examples with the strictly correct, disconnected Runner architecture.

***

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
yield return Compensate("OrderProcess"); // Runner re-hydrates and executes compiled delegates as if no time passed
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

## 3. Architecture: How the Disconnected Runner Handles Compensation

The most critical architectural constraint of the engine is that **the Orchestrator is purely for I/O and persistence**, and **the Runner is a pure, disconnected compute unit**. 

Because the `WorkflowExecutionRequest` provides the Runner with the complete `WorkflowStateDto` (including the full active Wait Tree and serialized state), the Runner processes rollbacks entirely in-memory without ever querying the database during execution.

### Phase 1: Registration (Building the Local Tree)
1.  **Execution:** As the Runner executes sequential commands (e.g., `ProcessPayment`), it evaluates `.RegisterCompensation(delegate)`.
2.  **State Mutation:** The Runner does *not* execute this delegate. Instead, it attaches the compensation blueprint (the compiled delegate identifier and the actual `CommandResult` payload) directly to that specific command's node within its in-memory **Wait Tree**.
3.  **Persistence:** When the Runner finishes its forward execution and hits a passive wait, it returns the mutated wait tree to the Orchestrator. The Orchestrator blindly saves this rich tree to the database.

### Phase 2: Triggering the Unwind (Local Traversal)
1.  **The Yield:** A failure occurs, and the workflow evaluates `yield return Compensate("CarRentalSaga_123")`.
2.  **Runner Interception:** The Runner identifies this as an active command. Instead of halting and going to the database, the Runner immediately begins a reverse traversal of its in-memory Wait Tree.
3.  **Locating Targets:** It scans the tree for any completed command nodes that contain the `"CarRentalSaga_123"` token in their token array. 

### Phase 3: The Double-Undo Fix & Execution (Runner-Side)
Because a command might belong to multiple scopes (e.g., `["Global", "Local"]`), the Runner must natively ensure idempotency before executing anything.
1.  **Checking Status:** For every matched node in the wait tree, the Runner checks an internal `IsCompensated` boolean flag on that node.
2.  **LIFO Execution:** The Runner sorts the valid, uncompensated nodes in **Last-In, First-Out (reverse chronological)** order.
3.  **Execution:** The Runner invokes the compiled compensation delegates sequentially, injecting the historical results directly into them from the local node data.
4.  **Marking Complete:** As each delegate succeeds, the Runner immediately updates the node's flag to `IsCompensated = true` within the local wait tree.

### Phase 4: Finalizing and Orchestrator Persistence
1.  **State Advancement:** Once the compensation stack for that specific token is fully unwound, the Runner advances the C# state machine to the next line of code after the `Compensate()` yield.
2.  **Return to Orchestrator:** When the workflow finally reaches a new passive wait (or completes), the Runner packages the updated `WorkflowStateDto` (which now contains the `IsCompensated = true` flags).
3.  **Dumb Persistence:** The Runner sends this finalized DTO back to the Orchestrator. The Orchestrator does no business logic; it simply commits the mutated wait tree and state snapshot to the database in a single transaction.