using System.Threading.Tasks;
namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface IDatabaseCleaning
    {
        Task CleanCompletedWorkflowInstances();
        Task MarkInactiveWaitTemplates();
        Task CleanInactiveWaitTemplates();
        Task CleanSoftDeletedRows();
        Task CleanOldSignals();

        //todo: Task DeleteInactiveMethodidentifiers();
    }
}
