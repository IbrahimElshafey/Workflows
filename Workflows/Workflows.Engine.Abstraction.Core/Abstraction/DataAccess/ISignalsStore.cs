using Workflows.Handler.InOuts.Entities;
using System.Threading.Tasks;
namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface ISignalsStore
    {
        Task<SignalEntity> GetSignal(long signalId);
        Task SaveSignal(SignalEntity signal);
        Task<bool> IsSignalAlreadyMatchedToWorkflow(long signalId, int rootWorkflowId);
    }
}