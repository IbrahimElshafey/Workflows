using System;
using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Runner
{
    public interface ICommandHandler
    {
        Task<object> ExecuteAsync(object commandContext);
    }
}
