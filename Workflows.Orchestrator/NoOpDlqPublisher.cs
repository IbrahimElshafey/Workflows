using System.Threading.Tasks;
using Workflows.Abstraction.Persistence;

namespace Workflows.Orchestrator
{
    /// <summary>
    /// Default no-op implementation of <see cref="IDlqPublisher"/>.
    /// Registers in DI so no concrete broker configuration is required out of the box.
    /// Replace with a real implementation (e.g., RabbitMQ, Azure Service Bus, Kafka)
    /// by registering an <see cref="IDlqPublisher"/> before calling AddWorkflows().
    /// </summary>
    internal sealed class NoOpDlqPublisher : IDlqPublisher
    {
        public Task PublishAsync(DlqMessage message)
        {
            // Intentional no-op. DLQ entries are always persisted to the DlqMessages table
            // in the WorkflowStore before this is called, so no data is lost.
            return Task.CompletedTask;
        }
    }
}
