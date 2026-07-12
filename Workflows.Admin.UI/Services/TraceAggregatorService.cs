using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Admin.UI.Models;
using Workflows.Storage.EntityFrameworkCore;

namespace Workflows.Admin.UI.Services
{
    public class TraceAggregatorService : ITraceAggregatorService
    {
        private readonly WorkflowsDbContext _dbContext;

        public TraceAggregatorService(WorkflowsDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<TraceViewModel?> GetExecutionTraceAsync(Guid instanceId, CancellationToken ct = default)
        {
            var instance = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == instanceId, ct);

            if (instance == null) return null;

            var entries = new List<TraceEntryViewModel>();

            // 1. Instance creation
            entries.Add(new TraceEntryViewModel
            {
                Timestamp = instance.Created,
                Type = "InstanceCreated",
                Title = "Instance Created",
                Description = $"Workflow '{instance.WorkflowType}' v{instance.WorkflowVersion} instance created."
            });

            // 2. Signal inbox events
            var signalInbox = await _dbContext.SignalInbox
                .AsNoTracking()
                .Where(s => s.WorkflowInstanceId == instanceId)
                .OrderBy(s => s.ProcessedAt)
                .ToListAsync(ct);

            foreach (var signal in signalInbox)
            {
                entries.Add(new TraceEntryViewModel
                {
                    Timestamp = signal.ProcessedAt,
                    Type = "SignalReceived",
                    Title = "Signal Received",
                    Description = $"Signal processed for instance {instanceId}.",
                    SignalIdentifier = signal.MessageId
                });
            }

            // 3. Command dispatch events from outbox
            var outboxMessages = await _dbContext.OutboxMessages
                .AsNoTracking()
                .Where(o => o.WorkflowInstanceId == instanceId)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync(ct);

            foreach (var msg in outboxMessages)
            {
                entries.Add(new TraceEntryViewModel
                {
                    Timestamp = msg.CreatedAt,
                    Type = "CommandDispatched",
                    Title = "Command Dispatched",
                    Description = $"Command {msg.CommandWaitId} dispatched.",
                    CommandWaitId = msg.CommandWaitId,
                    PayloadJson = msg.Payload
                });
            }

            // 4. Command result events
            var commandResults = await _dbContext.CommandResults
                .AsNoTracking()
                .Where(c => _dbContext.CommandWaits.Any(w => w.CommandWaitId == c.CommandWaitId && w.WorkflowInstanceId == instanceId))
                .OrderBy(c => c.ReceivedAt)
                .ToListAsync(ct);

            foreach (var result in commandResults)
            {
                entries.Add(new TraceEntryViewModel
                {
                    Timestamp = result.ReceivedAt,
                    Type = "CommandCompleted",
                    Title = result.IsSuccess ? "Command Completed" : "Command Failed",
                    Description = $"Command {result.CommandWaitId} result received.",
                    CommandWaitId = result.CommandWaitId,
                    ResultJson = result.ResultJson,
                    IsSuccess = result.IsSuccess
                });
            }

            // 5. Wait creation/completion from current wait tree
            CollectWaitTraceEntries(instance.Waits, entries);

            // 6. Cancellation history
            foreach (var cancellation in instance.CancellationHistory ?? new List<Workflows.Abstraction.DTOs.CancellationHistoryEntry>())
            {
                entries.Add(new TraceEntryViewModel
                {
                    Timestamp = cancellation.CancelledAt,
                    Type = "Cancellation",
                    Title = "Cancellation Triggered",
                    Description = $"Token '{cancellation.Token}' cancelled. Reason: {cancellation.Reason}"
                });
            }

            entries = entries.OrderBy(e => e.Timestamp).ToList();

            return new TraceViewModel
            {
                InstanceId = instanceId,
                WorkflowType = instance.WorkflowType,
                Entries = entries
            };
        }

        private static void CollectWaitTraceEntries(List<WaitInfrastructureDto>? waits, List<TraceEntryViewModel> entries)
        {
            if (waits == null) return;

            foreach (var wait in waits)
            {
                entries.Add(new TraceEntryViewModel
                {
                    Timestamp = wait.Created,
                    Type = "WaitCreated",
                    Title = $"{wait.WaitType} Created",
                    Description = $"Wait '{wait.WaitName}' created at line {wait.InCodeLine}.",
                    WaitId = wait.Id
                });

                if (wait.Status != WaitStatus.Waiting)
                {
                    entries.Add(new TraceEntryViewModel
                    {
                        Timestamp = wait.Created, // We don't have completion timestamp; use Created as fallback
                        Type = "WaitCompleted",
                        Title = $"{wait.WaitType} {wait.Status}",
                        Description = $"Wait '{wait.WaitName}' reached status {wait.Status}.",
                        WaitId = wait.Id,
                        IsSuccess = wait.Status == WaitStatus.Completed || wait.Status == WaitStatus.Matched
                    });
                }

                CollectWaitTraceEntries(wait.ChildWaits, entries);

                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    CollectWaitTraceEntries(externalGroup.ExternalChildWaits, entries);
                }
            }
        }
    }
}
