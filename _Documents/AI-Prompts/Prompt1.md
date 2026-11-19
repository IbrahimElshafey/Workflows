You are an expert C# developer writing workflows for a custom Snapshot-Serialized Workflow Engine.
Your task is to write the workflow container class, custom state class, and event/command/result POCOs for a given requirement.

### 🚫 Scope Restrictions (DO NOT WRITE IMPLEMENTATION)
1. **No Handlers/Workers:** Do not write command handler execution code.
2. **No Emitters/Publishers:** Do not write code emitting events/signals.
3. **Pure DSL & POCOs Only:** Generate only the `WorkflowContainer` subclass (with `Run` method DSL) and the POCO classes representing state, event payloads, command payloads, and command results.

### 🏛️ Core Architectural Rules
1. **Sealed Class:** Workflows must inherit from `WorkflowContainer` and be marked `sealed`.
2. **State Snapshotting:** Container properties are automatically snapshot-serialized. Keep state lightweight (store keys/IDs like `FileId`, not binary streams/PDF bytes).
3. **Execution Model:** The main method (usually `Run`) returns `IAsyncEnumerable<Wait>` using `yield return`.
4. **POCO-First:** All events, commands, results, and state parameters must be clean POCOs.
5. **C# & DI (Interfaces):** Use normal C# constructs. You can inject dependencies as interfaces via the workflow constructor.
6. **Encapsulated Readability:** Keep the main `Run` method clean. Encapsulate verbose builder chains (like `.ExecuteDeferred` with retries/result handlers) in helper methods returning `Wait` or a builder type (e.g., `yield return TypesetRtlPdf(...)`).

---

### 🛠️ DSL Primitives & Builders
*   `WaitSignal<TSignal>(identifier, name)`: Suspend for `TSignal`. Supports `.WithState()`, `.MatchIf()`, `.AfterMatch()`, `.WithCancelToken()`, `.OnCanceled()`.
*   `WaitDelay(TimeSpan, name)` / `WaitUntil(DateTime, name)`: Suspend for timer.
*   `ExecuteImmediate<TCommand, TResult>(name, data)`: Short-lived secure actions or database operations.
*   `ExecuteDeferred<TCommand, TResult>(name, data)`: Long-running async actions (e.g., processing/translation).
    *   *Command builders support:* `.WithState()`, `.WithRetries()`, `.OnResult()`, `.RegisterCompensation(async callback)`.
*   `WaitGroup(Wait[], name)` / `WaitMany(...)` / `WaitAny(...)`: Parallel execution groups. `WaitAny` prunes siblings on match.
*   `WaitSubWorkflow(stream, name)`: Nested sub-workflow.

---

### 📝 Defining Command POCOs & Reference Implementation

*   **Immediate Commands:** Implement `IImmediateCommand<TCommand, TResult>`. E.g.:
    ```csharp
    public class SendEmailCommand : IImmediateCommand<SendEmailCommand, SendEmailResult>
    {
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
    }
    public record SendEmailResult(string MessageId, bool Success);
    ```
*   **Deferred Commands:** Implement `IDeferredCommand<TCommand, TResult>`. Must define an explicit interface implementation for `MatchingFunction` to correlate the async event result back to the command:
    ```csharp
    public class ReserveInventoryCommand : IDeferredCommand<ReserveInventoryCommand, InventoryReservationResult>
    {
        public string OrderId { get; set; } = string.Empty;
        public int Quantity { get; set; }

        System.Linq.Expressions.Expression<System.Func<ReserveInventoryCommand, InventoryReservationResult, bool>> 
            IDeferredCommand<ReserveInventoryCommand, InventoryReservationResult>.MatchingFunction => 
                (cmd, result) => result.OrderId == cmd.OrderId;
    }
    public record InventoryReservationResult(string OrderId, bool Success, string ReservationId);
    ```

#### Blueprint Example:
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Definition;
using Workflows.Abstraction.Runner;

namespace MyApp.Workflows
{
    // POCO Events
    public record OrderSubmittedEvent(int OrderId, string CustomerEmail, int OrderQuantity);
    public class OrderProcessingState { }

    // Workflow Container
    [Workflow("OrderProcessingWorkflow", 1)]
    public sealed class OrderProcessingWorkflow : WorkflowContainer
    {
        public int CurrentOrderId { get; set; }
        public string CustomerEmail { get; set; } = string.Empty;
        public int OrderQuantity { get; set; }

        public OrderProcessingWorkflow(ILogger logger) { } // DI is supported

        public async IAsyncEnumerable<Wait> Run(OrderProcessingState state)
        {
            yield return WaitSignal<OrderSubmittedEvent>("OrderSubmittedSignal", "WaitSubmit")
                .MatchIf(o => o.OrderId > 0)
                .AfterMatch(o => { CurrentOrderId = o.OrderId; CustomerEmail = o.CustomerEmail; OrderQuantity = o.OrderQuantity; });

            yield return ReserveInventory(CurrentOrderId, OrderQuantity); // Encapsulated helper
        }

        private Wait ReserveInventory(int orderId, int quantity)
        {
            return ExecuteDeferred<ReserveInventoryCommand, InventoryReservationResult>(
                "ReserveInventory", new ReserveInventoryCommand { OrderId = orderId.ToString(), Quantity = quantity })
                .WithRetries(3, TimeSpan.FromSeconds(15))
                .OnResult((res, _) => Console.WriteLine($"Reserved: {res.ReservationId}"))
                .RegisterCompensation(async (res, _) => await Task.CompletedTask);
        }
    }
}
```

---

### 📝 Your Task

Write a high-level workflow named `PdfTranslationWorkflow` (Version 1) to translate a PDF file from English to Arabic. The workflow should start when the signal `Pdf_Translation_Requested` is received, you should did what senior translator will do but in programmable way, Later we will code every command and signals used. 

Keep the following requirements in mind, but design the execution flow, step boundaries, and command/signal payloads yourself:
1. **Lightweight State:** Keep the workflow container state lightweight. Do not store full files or byte arrays; store pointers/IDs (e.g. `FileId`, `WorkspaceId`).
2. **Command Usage:** Use commands for both long-running async steps (e.g. extraction, translation, PDF assembly) and short-lived secure actions (e.g. acquiring a workspace lease or registering an audit lock).
3. **Compensations:** Include a compensation step to release the secure action's resources (e.g. release the workspace lease or audit lock) if any subsequent step fails or is cancelled.

**Adhere strictly to the scope rules:** Generate only the sealed workflow class, the state class, and the POCOs for the events, commands, and results. Do not write command handlers, database repositories, or signal emitters. Use standard C# features normally.
