
using System;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Common
{
    public interface IMessageSubscriber
    {
        /// <summary>
        /// Used by the Runner to start listening for execution requests.
        /// </summary>
        void Subscribe<T>(string source, Func<T, Task> handler);
    }
}