using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Runner;

namespace Workflows.Hosting.InProcess
{
    public class RunnerWorker : BackgroundService
    {
        private readonly WorkflowExecutionChannel _channel;
        private readonly IServiceProvider _serviceProvider;

        public RunnerWorker(WorkflowExecutionChannel channel, IServiceProvider serviceProvider)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var reader = _channel.IngressReader;

            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (reader.TryRead(out var context))
                {
                    try
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            // Initialize the session for the scope
                            var session = scope.ServiceProvider.GetRequiredService<WorkflowExecutionSession>();
                            session.CompletionSource = context.CompletionSource;

                            // Resolve the concrete runner
                            var runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner>();

                            if (context.Message is WorkflowExecutionRequest req)
                            {
                                var versionRouter = scope.ServiceProvider.GetRequiredService<WorkflowVersionRouter>();
                                var sxsResult = await versionRouter.RouteAsync(req, stoppingToken);

                                if (sxsResult != null)
                                {
                                    if (context.CompletionSource != null)
                                    {
                                        context.CompletionSource.TrySetResult(sxsResult);
                                    }
                                    continue;
                                }

                                var result = await runner.RunWorkflowAsync(req);
                                // If the egress client was skipped or bypassed (e.g. Unmatched signal), 
                                // set result here directly so it does not hang
                                if (result.Status == "Unmatched" && context.CompletionSource != null)
                                {
                                    context.CompletionSource.TrySetResult(result);
                                }
                            }
                            else if (context.Message is StartWorkflowRequest startReq)
                            {
                                var result = await runner.StartWorkflow(startReq.WorkflowName, startReq.Version, startReq.Input);
                                // Set result directly if it was synchronous starting with no wait
                                // Wait, starting a workflow always yields the start result.
                                // In the case of StartWorkflow, does the Egress Channel client get called?
                                // Let's check: WorkflowRunner.StartWorkflow calls SendWorkflowRunResultAsync!
                                // Yes, it does. So Egress client will be called.
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (context.CompletionSource != null)
                        {
                            context.CompletionSource.TrySetException(ex);
                        }
                    }
                }
            }
        }
    }
}
