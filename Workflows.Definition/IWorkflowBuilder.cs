using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Shared.DataObject;

namespace Workflows.Definition
{
    public interface IWorkflowBuilder
    {
        // Generic registration relies on the Type name or attributes for metadata
        IWorkflowBuilder RegisterWorkflow<WorkflowClass>(
            IAsyncEnumerable<Wait> workflow,
            string name,
            string version = "1.0.0") where WorkflowClass : WorkflowContainer;

        IWorkflowBuilder RegisterSignal<TSignal>(string signalIdentifier);

        IWorkflowBuilder RegisterCommand<TCommand, TResult>(string commandIdentifier);

        IWorkflowBuilder RegisterRunner(string runnerName);
        IWorkflowBuilder SettingsSection(string settingsSection);

        // Finalize and push to the Orchestrator
        Task<RegistrationSyncResult> CommitAsync();
    }
}
