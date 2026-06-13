using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs;
using Workflows.Storage.EntityFrameworkCore;

namespace Workflows.Hosting.InProcess
{
    public class InboxPollerWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(500);

        public InboxPollerWorker(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollInboxAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[INBOX POLLER ERROR]: {ex}");
                }

                await Task.Delay(_pollInterval, stoppingToken);
            }
        }

        private async Task PollInboxAsync(CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<Workflows.Orchestrator.Orchestrator>();

                var pendingResults = await dbContext.CommandResults
                    .Where(r => r.Status == 0) // 0 = Pending
                    .OrderBy(r => r.ReceivedAt)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                if (pendingResults.Count == 0) return;

                foreach (var result in pendingResults)
                {
                    try
                    {
                        var dto = new CommandResultDto
                        {
                            CommandWaitId = result.CommandWaitId,
                            Result = string.IsNullOrEmpty(result.ResultJson) 
                                ? null! 
                                : JsonConvert.DeserializeObject<object>(result.ResultJson)!,
                            ClientSentTime = result.ReceivedAt,
                            OrchestratorReceiveTime = DateTime.UtcNow
                        };

                        using (var execScope = _serviceProvider.CreateScope())
                        {
                            var execOrchestrator = execScope.ServiceProvider.GetRequiredService<Workflows.Orchestrator.Orchestrator>();
                            await execOrchestrator.ProcessCommandResultAsync(dto);
                        }

                        result.Status = 1; // Processed
                        result.ProcessedAt = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[INBOX POLLER LOOP ERROR]: {ex}");
                        result.Status = 2; // Failed
                        result.ProcessedAt = DateTime.UtcNow;
                    }
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
