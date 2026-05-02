using System.Linq;
using System.Threading.Tasks;
using Workflows.Definition.Data.DTOs;

namespace Workflows.Definition
{
    public interface IWorkflowRegister
    {
        // Generic registration relies on the Type name or attributes for metadata
        IWorkflowRegister RegisterWorkflow<TWorkflow>(string version = "1.0.0") where TWorkflow : WorkflowContainer;

        IWorkflowRegister RegisterSignal<TSignal>(string explicitName = null);

        IWorkflowRegister RegisterCommand<TCommand, TResult>();

        // Physical runner check-in
        IWorkflowRegister RegisterRunner(string runnerId, params string[] listeningQueues);

        // Finalize and push to the Orchestrator
        Task<RegistrationSyncResult> CommitAsync();
    }
}