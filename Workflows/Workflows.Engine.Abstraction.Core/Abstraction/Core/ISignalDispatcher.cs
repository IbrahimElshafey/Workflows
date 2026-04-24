using Workflows.Handler.InOuts.Entities;
using System.Threading.Tasks;
namespace Workflows.Handler.Core.Abstraction
{
    /// <summary>
    /// Enqueue signal to be processed by engine
    /// </summary>
    public interface ISignalDispatcher
    {
        /// <summary>
        /// Signal coming from same code that is hosted by RF engine service
        /// </summary>
        Task<long> EnqueueLocalSignalWork(SignalEntity signal);

        /// <summary>
        /// Signal comming from external to the engine through API call
        /// </summary>
        Task<long> EnqueueExternalSignalWork(SignalEntity signal, string serviceName);
    }
}