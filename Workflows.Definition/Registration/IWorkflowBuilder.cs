using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Primitives;

namespace Workflows.Definition.Registration
{
    public interface IWorkflowBuilder
    {
        // Generic registration relies on the Type name or attributes for metadata
        IWorkflowBuilder RegisterWorkflow<WorkflowClass>(
            string name,
            string version) where WorkflowClass : WorkflowContainer;

        IWorkflowBuilder RegisterSignal<TSignal>(string signalIdentifier);

        IWorkflowBuilder RegisterCommand<TCommand, TResult>(string commandIdentifier, TimeSpan timeout = default, CommandExecutionMode mode = CommandExecutionMode.ImmediateCommand);

        IWorkflowBuilder RegisterRunner(string runnerName);
        IWorkflowBuilder SettingsSection(string settingsSection);

        // Finalize and push to the Orchestrator
        Task<RegistrationSyncResult> CommitAsync();
    }
}
