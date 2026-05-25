# Workflows.Definition

## 1. What is this?
The developer-facing Domain Specific Language (DSL) framework (targeting `netstandard2.1`) for declaring workflows. It provides a set of fluent builders and containers to define state machines, command execution steps, signals, and compensation actions.

Key features:
- `WorkflowContainer`: The base class inherited by all workflow definitions.
- Fluent Builders: `CommandBuilder`, `SignalWait`, `WaitGroup`, and delay options for defining execution flows.
- Assembly Scanning: DI extensions to automatically scan and register workflows.

## 2. How to use?
Define workflows by inheriting from `WorkflowContainer` and overriding the `Run` method. You yield-return `Wait` instances which describe the engine state boundaries.

### Example:
```csharp
using System.Collections.Generic;
using Workflows.Definition;

public sealed class OrderProcessingWorkflow : WorkflowContainer
{
    public override async IAsyncEnumerable<Wait> Run()
    {
        // Wait for an OrderSubmitted signal
        yield return WaitSignal<OrderSubmitted>("OrderSubmitted")
            .MatchIf(evt => evt.TotalAmount > 0);

        // Run a command to charge the credit card
        yield return RunCommand("ChargeCard", new ChargeCardCommand { Amount = 100 });
    }
}
```

### Registering Workflows:
```csharp
services.AddWorkflows(setup =>
{
    setup.RegisterFromAssemblyContaining<OrderProcessingWorkflow>("1.0.0");
});
```
