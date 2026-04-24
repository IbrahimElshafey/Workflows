using Microsoft.EntityFrameworkCore;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;

namespace Workflows.Handler.DataAccess
{
    internal class DatabaseCleaning : IDatabaseCleaning
    {
        private readonly WaitsDataContext _context;
        private readonly ILogsRepo _logsRepo;
        private readonly IWorkflowsSettings _setting;

        public DatabaseCleaning(
            WaitsDataContext context,
            ILogsRepo logsRepo,
            IWorkflowsSettings setting)
        {
            _context = context;
            _logsRepo = logsRepo;
            _setting = setting;
        }

        public async Task CleanCompletedWorkflowInstances()
        {
            await AddLog("Start to delete compeleted workflows instances.");
            var dateThreshold = DateTime.UtcNow.Subtract(_setting.CleanDbSettings.CompletedInstanceRetention);

            var instanceIds =
                await _context.WorkflowInstances
                .Where(instance => instance.Status == WorkflowInstanceStatus.Completed && instance.Modified < dateThreshold)
                .Select(x => x.Id)
                .ToListAsync();
            if (instanceIds.Any())
            {
                using var transaction = _context.Database.BeginTransaction();
                var waitsCount = await _context.Waits
                  .Where(wait => instanceIds.Contains(wait.WorkflowInstanceId))
                  .ExecuteDeleteAsync();

                var privateDataCount = await _context.PrivateData
                  .Where(privateData => instanceIds.Contains(privateData.WorkflowInstanceId.Value))
                  .ExecuteDeleteAsync();

                var instancesCount = await _context.WorkflowInstances
                    .Where(workflowInstance => instanceIds.Contains(workflowInstance.Id))
                    .ExecuteDeleteAsync();

                var logsCount = await _context.Logs
                    .Where(logItem => 
                            instanceIds.Contains((int)logItem.EntityId) && logItem.EntityType == EntityType.WorkflowInstanceLog)
                    .ExecuteDeleteAsync();

                var waitProcessingCount = await _context.WaitProcessingRecords
                    .Where(waitProcessingRecord => instanceIds.Contains(waitProcessingRecord.StateId))
                    .ExecuteDeleteAsync();
                transaction.Commit();
                
                await _logsRepo.AddLog(
                    $"* Delete [{privateDataCount}] private data record.\n"+
                    $"* Delete [{logsCount}] logs related to completed workflows instances done.\n"+
                    $"* Delete [{instancesCount}] compeleted workflows instances done.\n"+
                    $"* Delete [{waitsCount}] waits related to completed workflows instances done.\n"+
                    $"* Delete [{waitProcessingCount}] wait processing record related to completed workflows instances done.",
                    LogType.Info,
                    StatusCodes.DataCleaning);
            }
            await AddLog("Delete compeleted workflows instances completed.");
        }

        public async Task CleanOldSignals()
        {
            await AddLog("Start to delete old pushed calls.");
            var dateThreshold = DateTime.UtcNow.Subtract(_setting.CleanDbSettings.SignalRetention);
            var count =
                await _context.Signals
                .Where(instance => instance.Created < dateThreshold)
                .ExecuteDeleteAsync();
            await AddLog($"Delete [{count}] old pushed calls.");
        }

        public async Task CleanSoftDeletedRows()
        {
            await AddLog("Start to delete soft deleted rows.");

            var count = await _context.Waits
             .Where(instance => instance.IsDeleted)
             .IgnoreQueryFilters()
             .ExecuteDeleteAsync();
            await AddLog($"Delete [{count}] soft deleted waits done.");

            count = await _context.WorkflowInstances
            .Where(instance => instance.IsDeleted)
            .IgnoreQueryFilters()
            .ExecuteDeleteAsync();
            await AddLog($"Delete [{count}] soft deleted workflow state done.");
        }

        public async Task MarkInactiveWaitTemplates()
        {
            await AddLog("Start to deactivate unused wait templates.");
            var activeWaitTemplate =
                _context.MethodWaits
                .Where(x => x.Status == WaitStatus.Waiting)
                .Select(x => x.TemplateId)
                .Distinct();
            var count = await _context.WaitTemplates
                .Where(waitTemplate => waitTemplate.IsActive == 1 && !activeWaitTemplate.Contains(waitTemplate.Id))
                .ExecuteUpdateAsync(template => template
                    .SetProperty(x => x.IsActive, -1)
                    .SetProperty(x => x.DeactivationDate, DateTime.UtcNow));
            await AddLog($"Deactivate [{count}] unused wait templates done.");
        }

        public async Task CleanInactiveWaitTemplates()
        {
            await AddLog("Start to delete deactivated wait templates.");
            var dateThreshold = DateTime.UtcNow.Subtract(_setting.CleanDbSettings.DeactivatedWaitTemplateRetention);
            var count = await _context.WaitTemplates
                .Where(template => template.IsActive == -1 && template.DeactivationDate < dateThreshold)
                .ExecuteDeleteAsync();
            await AddLog($"Delete [{count}] deactivated wait templates done.");
        }

        private async Task AddLog(string message)
        {
            await _logsRepo.AddLog(message, LogType.Info, StatusCodes.DataCleaning);
        }
    }
}
