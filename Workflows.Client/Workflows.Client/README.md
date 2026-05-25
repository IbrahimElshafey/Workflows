# Workflows.Client

## 1. What is this?
The standard client SDK (targeting `net10.0`) for worker nodes and external systems interacting with the workflows engine.

Key capabilities:
- **Signal Dispatching**: Exposes `IWorkflowSignalClient` to allow external business flows to post signals and resume waiting workflow state machines.
- **Command Execution Worker**: Exposes `ICommandExecutor<TCommand, TResult>` interfaces and hosts the `CommandExecutorWorker` background service to receive and execute deferred asynchronous commands scheduled by the workflow orchestrator.

## 2. How to use?
Reference this project in your external microservices or worker nodes, and register commands that this service is responsible for executing.

### Example (Registering a Command Executor):
```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Client;

var builder = WebApplication.CreateBuilder(args);

// Register client and register a command executor for sending emails
builder.Services.AddWorkflowsClient()
    .AddCommandExecutor<SendEmailCommand, SendEmailResult, SendEmailExecutor>("EmailService.Send");
```

### Implementing `ICommandExecutor`:
```csharp
public class SendEmailExecutor : ICommandExecutor<SendEmailCommand, SendEmailResult>
{
    public async Task<SendEmailResult> ExecuteAsync(SendEmailCommand command, CancellationToken cancellationToken)
    {
        // Execute business logic (e.g. SMTP call)
        return new SendEmailResult { Success = true };
    }
}
```
