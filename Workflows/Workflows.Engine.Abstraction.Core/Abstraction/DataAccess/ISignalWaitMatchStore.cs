using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface ISignalWaitMatchStore
    {
        SignalWaitMatch Add(SignalWaitMatch waitProcessingRecord);
    }
}