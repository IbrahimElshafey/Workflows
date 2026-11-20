using System.Threading.Tasks;

namespace Workflows.Abstraction.Persistence
{
    /// <summary>
    /// Default no-op implementation of <see cref="IDlqPublisher"/>.
    /// Registered with <c>TryAddSingleton</c> so an application-supplied publisher registered
    /// beforehand always wins. DLQ entries are still persisted to the <c>DlqMessages</c> table
    /// by the compensation worker, so no data is lost when this implementation is active.
    /// </summary>
    public sealed class NoOpDlqPublisher : IDlqPublisher
    {
        public Task PublishAsync(DlqMessage message)
        {
            return Task.CompletedTask;
        }
    }
}