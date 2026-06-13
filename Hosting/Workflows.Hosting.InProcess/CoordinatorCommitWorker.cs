using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workflows.Abstraction.DTOs;
using Workflows.Orchestrator;

namespace Workflows.Hosting.InProcess
{
    public class CoordinatorCommitWorker : BackgroundService
    {
        private readonly WorkflowExecutionChannel _channel;
        private readonly IServiceProvider _serviceProvider;

        public CoordinatorCommitWorker(WorkflowExecutionChannel channel, IServiceProvider serviceProvider)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var reader = _channel.EgressReader;

            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (reader.TryRead(out var delta))
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            // Resolve the concrete DB writer client directly
                            var dbClient = scope.ServiceProvider.GetRequiredService<WorkflowRunnerClient>();

                            var committedResult = await dbClient.SendWorkflowRunResultAsync(
                                delta.RunResult,
                                delta.Response,
                                stoppingToken);

                            if (delta.CompletionSource != null)
                            {
                                delta.CompletionSource.TrySetResult(committedResult);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (delta.CompletionSource != null)
                        {
                            delta.CompletionSource.TrySetException(ex);
                        }
                    }
                }
            }
        }
    }
}
