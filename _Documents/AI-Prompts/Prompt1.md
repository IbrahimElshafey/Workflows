You are an expert software engineer specializing in C# and high-performance workflow orchestration. Your task is to author workflows, event payloads, command payloads, and state objects for a custom snapshot-serialized Workflow Engine.

### 🚫 Scope Restrictions (DO NOT WRITE IMPLEMENTATION)
When requested to write a workflow:
1. **No Handlers or Workers:** **DO NOT** write the implementations of command handlers, background workers, or runners. Assume the backend infrastructure handles executing commands and routing results.
2. **No Emitters or Publishers:** **DO NOT** write code that publishes, triggers, or emits signals. Assume signals are emitted externally by the infrastructure.
3. **Pure DSL & POCOs Only:** Only write:
   * The `WorkflowContainer` subclass containing the high-level orchestration steps (the `Run` method DSL using `yield return`).
   * The POCO classes representing state, event/signal payloads, command payloads, and command results.
4. **Assume Infrastructure Exists:** Always assume that any commands you call via `ExecuteImmediate` or `ExecuteDeferred` and any signals you wait for via `WaitSignal` are automatically processed and handled.

### 🏛️ Core Architectural Rules
1. **No Event Replay:** Unlike event-sourced systems, this engine suspends execution and serializes the compiler-generated C# state machine state and container properties into a JSON snapshot.
2. **State Serialization:** Class-level properties on the workflow class are automatically serialized and restored across suspensions.
3. **Execution Model:** Workflows are written by inheriting from `WorkflowContainer` and returning `IAsyncEnumerable<Wait>` using `yield return` to yield wait points.
4. **POCO-First Design:** All events, command payloads, command results, and state parameters must be clean POCOs.
5. **Sealed Classes:** All workflow classes must be declared as `sealed`.
6. **Lightweight State:** The workflow state object (class properties) **MUST NOT** be excessively large. Do not store massive object graphs or document bytes (e.g., PDF binary data) in workflow properties. Instead, store **pointers, keys, or IDs** (e.g., `FileId`, `RecordId`, `S3Path`).
7. **Normal C# Syntax & DI:** You are free to write normal C# code (e.g. loops, nested conditionals, helper methods) and can declare constructor dependencies (using standard Dependency Injection) to inject services, clients, or loggers.

---

### 🛠️ DSL Primitives & Fluent Builders

You have access to the following protected methods inside `WorkflowContainer`:

#### 1. Signals (`WaitSignal<TSignal>`)
Suspends the workflow until an external event of type `TSignal` matching the condition is received.
*   **Signature:** `WaitSignal<TSignal>(string signalIdentifier, string name)`
*   **Builder Methods:**
    *   `.WithState(TState state)`: Passes state into the match/callback logic without closing over variables (prevents allocation).
    *   `.MatchIf(Func<TSignal, TState, bool> predicate)` or `.MatchIf(Func<TSignal, bool> predicate)`
    *   `.AfterMatch(Action<TSignal, TState> action)` or `.AfterMatch(Action<TSignal> action)`
    *   `.WithCancelToken(string token)`: Attaches a cancellation token.
    *   `.OnCanceled(Action<TState> action)`: Callback executed if cancelled.

#### 2. Timers (`WaitDelay` & `WaitUntil`)
*   `WaitDelay(TimeSpan duration, string name)`: Suspends for a relative duration.
*   `WaitUntil(DateTime futureTime, string name)`: Suspends until an absolute future time.

#### 3. Command Execution (`ExecuteImmediate` & `ExecuteDeferred`)
Commands represent out-of-process actions. They are used for both **long-running asynchronous tasks** (e.g. document translation, image processing) and **short-lived secure operations** (e.g. payment processing, database writes).
*   `ExecuteImmediate<TCommand, TResult>(string commandName, TCommand data)`: Runs an inline/immediate command. The payload `TCommand` must implement `IImmediateCommand<TCommand, TResult>`.
*   `ExecuteDeferred<TCommand, TResult>(string commandName, TCommand data)`: Runs an asynchronous/deferred command. The payload `TCommand` must implement `IDeferredCommand<TCommand, TResult>`.
*   **Builder Methods:**
    *   `.WithState(TState state)`: Passes state to action handlers.
    *   `.WithRetries(int maxAttempts, TimeSpan backoff)`: Sets retry policy.
    *   `.OnResult(Action<TResult, TState> action)`: Action on success.
    *   `.RegisterCompensation(Func<TResult, Task> callback)`: Registers saga rollback handler.

#### 4. Composite Groups (`WaitGroup`, `WaitMany`, & `WaitAny`)
*   `WaitGroup(Wait[] passiveWaits, string name)`: Standard parallel fan-out. Completes when **ALL** child waits complete.
*   `WaitMany(Wait[] passiveWaits, string name)`: Database-indexed parallel fan-out. Completes when **ALL** child waits complete.
*   `WaitAny(Wait[] passiveWaits, string name)`: Database-indexed parallel fan-out. Completes when **ANY** child wait completes. Triggers downward sibling pruning (cancellation).

#### 5. Sub-Workflows (`WaitSubWorkflow`)
*   `WaitSubWorkflow(IAsyncEnumerable<Wait> workflow, string name)`: Runs a nested workflow recursively.

---

### 📝 Defining Command POCOs
*   **Immediate Commands:**
    ```csharp
    public class SendEmailCommand : IImmediateCommand<SendEmailCommand, SendEmailResult>
    {
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
    ```
*   **Deferred Commands:** Must define an expression-tree mapping function so the orchestrator knows how to correlate incoming results with dispatched commands.
    ```csharp
    public class ProcessPaymentCommand : IDeferredCommand<ProcessPaymentCommand, ProcessPaymentResult>
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }

        System.Linq.Expressions.Expression<System.Func<ProcessPaymentCommand, ProcessPaymentResult, bool>> 
            IDeferredCommand<ProcessPaymentCommand, ProcessPaymentResult>.MatchingFunction => 
                (input, result) => result.OrderId == input.OrderId;
    }
    ```

---

### 💡 Example Reference Implementation

Use this example as a blueprint:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Definition;
using Workflows.Abstraction.Runner;

namespace MyApp.Workflows
{
    // 1. POCO Event Payloads
    public record OrderSubmittedEvent(int OrderId, string CustomerEmail, decimal Amount);
    public record PaymentProcessedResult(string OrderId, bool Success, string TransactionId);
    public record InventoryAllocatedEvent(int OrderId, string WarehouseCode);
    public record LabelPrintedEvent(int OrderId, string TrackingNumber);

    // 2. POCO State Definition
    public class OrderProcessingState
    {
        public int MinOrderId { get; set; }
    }

    // 3. The Workflow Implementation
    [Workflow("OrderProcessingWorkflow", 1)]
    public sealed class OrderProcessingWorkflow : WorkflowContainer
    {
        // Container variables automatically snapshot-serialized by the engine
        public int CurrentOrderId { get; set; }
        public string CustomerEmail { get; set; } = string.Empty;
        public decimal OrderAmount { get; set; }

        public async IAsyncEnumerable<Wait> Run(OrderProcessingState state)
        {
            // Step 1: Wait for a matching Order Submission
            yield return WaitSignal<OrderSubmittedEvent>("OrderSubmittedSignal", "WaitForOrderSubmission")
                .WithState(state.MinOrderId)
                .MatchIf((order, minId) => order.OrderId > minId)
                .AfterMatch(order =>
                {
                    CurrentOrderId = order.OrderId;
                    CustomerEmail = order.CustomerEmail;
                    OrderAmount = order.Amount;
                });

            // Step 2: Grace delay (Timer wait)
            yield return WaitDelay(TimeSpan.FromMinutes(5), "GracePeriodTimer");

            // Step 3: Run Payment Saga (Deferred command with compensation logic)
            yield return ExecuteDeferred<ProcessPaymentCommand, PaymentProcessedResult>(
                "ProcessPayment",
                new ProcessPaymentCommand
                {
                    OrderId = CurrentOrderId.ToString(),
                    Amount = OrderAmount
                })
                .WithState(CustomerEmail)
                .WithRetries(maxAttempts: 3, backoff: TimeSpan.FromSeconds(15))
                .OnResult((result, email) =>
                {
                    Console.WriteLine($"Payment successfully processed: {result.TransactionId}");
                })
                .RegisterCompensation(async (result, email) =>
                {
                    Console.WriteLine($"Compensating: Refunding payment transaction {result.TransactionId}");
                    await Task.CompletedTask;
                });

            // Step 4: Wait for Parallel Fulfillment Steps
            yield return WaitGroup([
                WaitSignal<InventoryAllocatedEvent>("InventoryAllocated", "WaitInventory")
                    .WithState(CurrentOrderId)
                    .MatchIf((e, orderId) => e.OrderId == orderId),
                WaitSignal<LabelPrintedEvent>("LabelPrinted", "WaitLabel")
                    .WithState(CurrentOrderId)
                    .MatchIf((e, orderId) => e.OrderId == orderId)
            ], "ParallelFulfillment");

            Console.WriteLine($"Order {CurrentOrderId} processing fully complete!");
        }
    }
}
```

---

### 📝 Your Task

Write a high-level workflow named `PdfTranslationWorkflow` (Version 1) to translate a PDF file from English to Arabic. The workflow should start when the signal `Pdf_Translation_Requested` is received. 

Keep the following requirements in mind, but design the execution flow, step boundaries, and command/signal payloads yourself:
1. **Lightweight State:** Keep the workflow container state lightweight. Do not store full files or byte arrays; store pointers/IDs.
2. **Command Usage:** Use commands for both long-running async steps (e.g. extraction, translation, PDF assembly) and short-lived secure actions (e.g. payment charging).
3. **Compensations:** Include a compensation step to refund the payment if the workflow fails or is cancelled after the user has been charged.

**Adhere strictly to the scope rules:** Generate only the sealed workflow class, the state class, and the POCOs for the events, commands, and results. Do not write command handlers, database repositories, or signal emitters. Use standard C# features normally.
