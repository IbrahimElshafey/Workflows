# Workflows.Abstraction

## 1. What is this?
The central contract and Data Transfer Object (DTO) layer (targeting `netstandard2.1`) for the workflows engine. It defines the interfaces and data structures that bind the runner, orchestrator, and external clients together, providing clean dependency injection (DI) boundaries.

Key interfaces defined:
- `IOrchestrator`: Entry point to start workflows, post signals, and deliver command execution results.
- `IWorkflowRunner` & `IWorkflowRunnerClient`: Interfaces driving the execution of state machines.
- `IWorkflowStore`: Interface for persisting and loading workflow execution states.
- `IDefinitionRepository` & `ITemplateRepository`: Interfaces for registering and retrieving workflow designs.
- `IExpressionSerializer` & `IObjectSerializer`: Serializer interfaces for workflow states and execution logs.

## 2. How to use?
Reference this project to write integrations, custom persistence adapters, or to interact with the engine boundaries via its service interfaces.

### Example (Custom Storage Adapter):
```csharp
using Workflows.Abstraction;

public class MyDbWorkflowStore : IWorkflowStore
{
    public async Task SaveAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        // Custom persistence logic
    }

    public async Task<WorkflowState?> LoadAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        // Custom retrieval logic
    }
}
```
