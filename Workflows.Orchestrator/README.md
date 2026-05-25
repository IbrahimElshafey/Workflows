# Workflows.Orchestrator

## 1. What is this?
The control plane orchestrator (targeting `net10.0`) of the workflows engine. It acts as the central coordinator for all workflow instances, routing signals and command results, managing persistence, and executing background timers for delayed steps.

Key responsibilities:
- **Instance Coordination**: Orchestrates workflow starting, pausing, compensation, and execution loops.
- **Background Scheduler**: Runs a polling scheduler (`Scheduler`) for resolving timeouts, delays, and recurring timers.
- **Persistence Handling**: Interfaces with database stores to commit and load workflow execution states.
- **Runner Proxying**: Dispatches actual step execution tasks to the `Workflows.Runner` over configured message transports.

## 2. How to use?
Inject `IOrchestrator` into your business logic, web APIs, or message handlers to interact with workflow instances.

### Example:
```csharp
using System;
using System.Threading.Tasks;
using Workflows.Abstraction;

public class OrderController
{
    private readonly IOrchestrator _orchestrator;

    public OrderController(IOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task StartOrderProcess(Guid orderId, decimal amount)
    {
        await _orchestrator.StartWorkflowAsync(
            name: "OrderProcessingWorkflow",
            version: "1.0.0",
            instanceId: orderId,
            input: new { Amount = amount }
        );
    }
}
```
