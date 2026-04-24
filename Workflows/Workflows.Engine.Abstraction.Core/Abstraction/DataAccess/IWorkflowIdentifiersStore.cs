using System.Threading.Tasks;

using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface IWorkflowIdentifiersStore
    {
        Task<WorkflowIdentifier> AddWorkflowIdentifier(MethodData methodData);
        Task<WorkflowIdentifier> GetWorkflowIdentifier(int id);
        Task<WorkflowIdentifier> GetWorkflowIdentifier(MethodData methodData);
    }
}