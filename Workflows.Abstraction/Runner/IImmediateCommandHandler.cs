using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Runner
{
    public interface IImmediateCommandHandler<in TCommand, TResult>
    {
        Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
    }
}
