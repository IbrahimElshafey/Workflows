# Workflows.Definition

## 1. What is this?
The developer-facing Domain Specific Language (DSL) framework (targeting `netstandard2.1`, **zero external dependencies**) for declaring workflows. It provides the fluent wait builders and the container base class used to author state machines as plain C# async iterators.

Key features:
- `WorkflowContainer`: the base class inherited by all workflow definitions. The engine discovers a `Run` method on it (name configurable via `WorkflowAttribute.StartMethod`).
- Wait primitives: `WaitSignal<T>`, `WaitDelay`, `WaitGroup` (`MatchAll`/`MatchAny`), `WaitSubWorkflow`, `ExecuteCommand` / `ExecuteDeferred`, and compensation helpers.
- `[Workflow(name, version)]`: marks a concrete container with the identity the engine routes on.
- Registration helpers: `IWorkflowBuilder.RegisterWorkflow<T>(...)`, `RegisterSignal<T>`, `RegisterCommand<TCommand, TResult>`, plus `RegisterFromAssemblyContaining<T>()` for attribute-based scanning.

## 2. How to use?

A workflow is a `sealed partial` container with its own `Run` method. It is **not** an override of a base `Run` — the base class only exposes `WaitsStates`, `TokensToCancel`, `CancelToken(...)`, `OnError` and `OnCompleted`. The method takes your own state object and yields `Wait` instances for each engine boundary:

```csharp
using System.Collections.Generic;
using Workflows.Definition;
using Workflows.Primitives;

public sealed class OrderState
{
    public bool StockOk { get; set; }
}

[Workflow("OrderProcessingWorkflow", 1)]
public sealed partial class OrderProcessingWorkflow : WorkflowContainer
{
    public string OrderId { get; set; } = string.Empty;

    public async IAsyncEnumerable<Wait> Run(OrderState state)
    {
        // Wait for a signal; the payload populates container state
        yield return WaitSignal<OrderSubmitted>("OrderSubmitted", "WaitOrderSubmitted")
            .MatchIf(evt => evt.TotalAmount > 0)
            .AfterMatch(sig => OrderId = sig.OrderId);

        // Deferred command — executed out-of-process, resumed by the command result
        yield return ExecuteDeferred<ChargeCardCommand, ChargeCardResult>(
            "ChargeCard",
            new ChargeCardCommand { Amount = 100 });

        // State-dependent wait
        yield return WaitSignal<StockConfirmed>("StockConfirmed", "WaitStock")
            .WithState(state)
            .MatchIf((sig, st) => sig.OrderId == OrderId);
    }
}
```

### Registering Workflows

Attribute-based scanning (recommended) — every concrete workflow in the assembly must carry `[Workflow]`, otherwise registration throws:

```csharp
using Workflows.Definition.Registration;

IWorkflowBuilder builder = ...;               // supplied by the host
builder.RegisterFromAssemblyContaining<OrderProcessingWorkflow>();
```

Explicit registration:

```csharp
builder.RegisterWorkflow<OrderProcessingWorkflow>("OrderProcessingWorkflow", 1);
builder.RegisterSignal<OrderSubmitted>("OrderSubmitted");
builder.RegisterCommand<ChargeCardCommand, ChargeCardResult>("ChargeCard", TimeSpan.FromMinutes(5));
await builder.CommitAsync();
```

> `Workflows.Definition.DI.RegisterWorkflows(...)` is a placeholder and currently throws `NotImplementedException`. Use the builder returned by the hosting package (`AddWorkflowsShared()` / `AddWorkflowsRunner()`) instead.