using System;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Executes fast, in-memory operations synchronously or via quick I/O.
    /// The workflow Runner awaits this and immediately continues execution.
    /// </summary>
    public interface IImmediateCommandHandler<in TCommand, TResult>
    {
        ValueTask<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Dispatches a command to an external system. 
    /// Does NOT return a result, as the workflow will suspend and wait for an asynchronous callback.
    /// </summary>
    public interface IDeferredCommandDispatcher<in TCommand>
    {
        ValueTask DispatchAsync(TCommand command, Guid commandId, Guid workflowInstanceId, CancellationToken cancellationToken);
    }

    public interface IWorkflowCommandContext
    {
        Guid WorkflowInstanceId { get; }
        Guid CommandId { get; }

        // Used by the Runner to set the context before execution
        void SetContext(Guid workflowInstanceId, Guid commandId);
    }

    // 2. INTERNAL WRITER (Used ONLY by the Runner/Factory)
    internal interface IWorkflowCommandContextSetter
    {
        void SetContext(Guid workflowInstanceId, Guid commandId);
    }
}
