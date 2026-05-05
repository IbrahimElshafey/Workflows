
using System;
using System.Threading.Tasks;
namespace Workflows.Shared.Communication
{
    public interface IMessageSubscriber : IDisposable
    {
        /// <summary>
        /// Used by the Runner to start listening for execution requests.
        /// </summary>
        void Subscribe<T>(Func<T, Task> handler);
    }
}