using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Runner
{
    /// <summary>
    /// Handles the execution of a command associated with an ICommandWait.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>
        /// Executes the command represented by the given wait within the provided workflow context.
        /// </summary>
        Task ExecuteAsync(ICommandWait command, WorkflowExecutionRequest context);
    }
}
