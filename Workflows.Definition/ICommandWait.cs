using System;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Primitives;

namespace Workflows.Definition
{
    /// <summary>
    /// Marker interface for active waits that initiate side effects (e.g., commands, API calls).
    /// </summary>
    public interface ICommandWait
    {
        string HandlerKey { get; }
        CommandExecutionMode ExecutionMode { get; }
    }
}

