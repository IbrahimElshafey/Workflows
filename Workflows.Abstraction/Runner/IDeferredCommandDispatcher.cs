using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Runner
{
    public interface IDeferredCommandDispatcher<in TCommand>
    {
        Task DispatchAsync(TCommand command, CancellationToken cancellationToken = default);
    }
}
