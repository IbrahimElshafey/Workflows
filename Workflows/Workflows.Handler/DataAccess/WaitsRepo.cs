using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Expressions;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Linq.Expressions;

namespace Workflows.Handler.DataAccess;
internal partial class WaitsStore : IWaitsStore
{
    private readonly ILogger<WaitsStore> _logger;
    private readonly WaitsDataContext _context;
    private readonly IBackgroundProcess _backgroundJobClient;
    private readonly IMethodIdentifiersStore _methodIdsRepo;
    private readonly IWorkflowIdentifiersStore _workflowIdentifiersStore;
    private readonly IWorkflowsSettings _settings;
    private readonly IWaitTemplatesStore _waitTemplatesRepo;
    private readonly ILogsRepo _logsRepo;

    public WaitsStore(
        ILogger<WaitsStore> logger,
        IBackgroundProcess backgroundJobClient,
        WaitsDataContext context,
        IMethodIdentifiersStore methodIdentifierRepo,
        IWorkflowsSettings settings,
        IWaitTemplatesStore waitTemplatesRepo,
        ILogsRepo logsRepo,
        IWorkflowIdentifiersStore workflowIdentifiersStore)
    {
        _logger = logger;
        _context = context;
        _backgroundJobClient = backgroundJobClient;
        _methodIdsRepo = methodIdentifierRepo;
        _settings = settings;
        _waitTemplatesRepo = waitTemplatesRepo;
        _logsRepo = logsRepo;
        _workflowIdentifiersStore = workflowIdentifiersStore;
    }

    public async Task<PotentialSignalEffection> GetSignalEffectionInCurrentService(string methodUrn, DateTime puhsedSignalDate)
    {
        var methodGroup = await GetMethodGroup(methodUrn);
        var affectedWorkflows =
            await
            _context.MethodWaits
            .Where(x =>
                x.Status == WaitStatus.Waiting &&
                x.MethodGroupToWaitId == methodGroup.Id &&
                x.ServiceId == _settings.CurrentServiceId &&
                x.Created < puhsedSignalDate)
            .Select(x => x.RequestedByWorkflowId)
            .Distinct()
            .ToListAsync();
        return affectedWorkflows.Any() ?
            new PotentialSignalEffection
            {
                AffectedServiceId = _settings.CurrentServiceId,
                AffectedServiceUrl = string.Empty,
                AffectedServiceName = _settings.CurrentServiceName,
                MethodGroupId = methodGroup.Id,
                AffectedWorkflowsIds = affectedWorkflows,
            }
            : null;
    }

    public async Task<List<PotentialSignalEffection>> GetImpactedWorkflows(string methodUrn, DateTime puhsedSignalDate)
    {
        var methodGroup = await GetMethodGroup(methodUrn);

        var methodWaitsQuery = _context
                   .MethodWaits
                   .Where(x =>
                       x.Status == WaitStatus.Waiting &&
                       x.MethodGroupToWaitId == methodGroup.Id &&
                       x.Created < puhsedSignalDate);

        if (methodGroup.IsLocalOnly)
        {
            methodWaitsQuery = methodWaitsQuery.Where(x => x.ServiceId == _settings.CurrentServiceId);
        }

        var affectedWorkflowsGroupedByService =
            await methodWaitsQuery
           .Select(x => new { x.RequestedByWorkflowId, x.ServiceId })
           .Distinct()
           .GroupBy(x => x.ServiceId)
           .ToListAsync();

        return (
              from service in await _context.ServicesData.Where(x => x.ParentId == -1).ToListAsync()
              from affectedWorkflow in affectedWorkflowsGroupedByService
              where service.Id == affectedWorkflow.Key
              select new PotentialSignalEffection
              {
                  AffectedServiceId = service.Id,
                  AffectedServiceUrl = service.Url,
                  AffectedServiceName = service.AssemblyName,
                  MethodGroupId = methodGroup.Id,
                  AffectedWorkflowsIds = affectedWorkflow.Select(x => x.RequestedByWorkflowId).ToList(),
              }
              )
              .ToList();
    }


    private async Task<MethodsGroup> GetMethodGroup(string methodUrn)
    {
        var methodGroup =
           await _context
               .MethodsGroups
               .AsNoTracking()
               .Where(x => x.MethodGroupUrn == methodUrn)
               .FirstOrDefaultAsync();
        if (methodGroup != default)
            return methodGroup;
        var error = $"Method [{methodUrn}] is not registered in current database as [WaitMethod].";
        _logger.LogWarning(error);
        throw new Exception(error);
    }

    public async Task RemoveFirstWaitIfExist(int methodIdentifierId)
    {
        try
        {
            var firstWaitItems =
                 await _context.Waits
                .Where(x =>
                    x.IsFirst &&
                    x.RequestedByWorkflowId == methodIdentifierId)
                .ToListAsync();

            if (firstWaitItems != null)
            {
                foreach (var wait in firstWaitItems)
                {
                    wait.IsDeleted = true;
                    if (wait is MethodWaitEntity { MethodWaitType: MethodWaitType.TimeWaitMethod })
                    {
                        wait.LoadUnmappedProps();
                        var jobId = wait.ExtraData["JobId"];
                        _backgroundJobClient.Delete(jobId);
                    }
                }
                //todo:[update] load entity to delete it , concurrency control token and FKs
                if (firstWaitItems.FirstOrDefault()?.WorkflowInstanceId is int stateId)
                {
                    var workflowInstance = await _context
                     .WorkflowInstances
                     .FirstAsync(x => x.Id == stateId);
                    _context.WorkflowInstances.Remove(workflowInstance);
                }
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error when RemoveFirstWaitIfExist for workflow [{methodIdentifierId}]");
        }
    }


    public async Task CancelSubWaits(long parentId, long signalId)
    {
        await CancelChildWaits(parentId);

        async Task CancelChildWaits(long pId)
        {
            var waits = await _context
                .Waits
                .Include(x => x.ClosureData)
                .Where(x => x.ParentWaitId == pId && x.Status == WaitStatus.Waiting)
                .ToListAsync();

            foreach (var wait in waits)
            {
                CancelWait(wait, signalId);//CancelSubWaits
                if (wait.CanBeParent)
                    await CancelChildWaits(wait.Id);
            }
        }
    }

    private void CancelWait(WaitEntity wait, long signalId)
    {
        if (wait.ParentWait != null)//todo:traverse up to get current workflow
            wait.CurrentWorkflow = wait.ParentWait.CurrentWorkflow;
        wait.LoadUnmappedProps();
        wait.Cancel();
        wait.SignalId = signalId;

        bool isTimeWait = wait is MethodWaitEntity mw && mw.MethodWaitType == MethodWaitType.TimeWaitMethod;
        if (isTimeWait)
        {
            var jobId = wait.ExtraData["JobId"];
            _backgroundJobClient.Delete(jobId);
        }
    }

    public async Task<WaitEntity> GetWaitParent(WaitEntity wait)
    {
        if (wait?.ParentWaitId != null)
        {
            return await _context
                .Waits
                .Include(x => x.ChildWaits)
                .Include(x => x.RequestedByWorkflow)
                .FirstOrDefaultAsync(x => x.Id == wait.ParentWaitId);
        }
        return null;
    }


    public async Task CancelOpenedWaitsForState(int stateId)
    {
        await _context.Waits
              .Where(x => x.WorkflowInstanceId == stateId && x.Status == WaitStatus.Waiting)
              .ExecuteUpdateAsync(x => x.SetProperty(wait => wait.Status, _ => WaitStatus.Canceled));
    }

    public async Task<List<MethodWaitEntity>> GetPendingWaitsForTemplate(
        int templateId,
        string mandatoryPart,
        DateTime signalDate,
        params Expression<Func<MethodWaitEntity, object>>[] includes)
    {
        var query = _context
            .MethodWaits
            .Where(
                wait =>
                wait.Status == WaitStatus.Waiting &&
                wait.TemplateId == templateId &&
                wait.Created < signalDate);
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        if (mandatoryPart != null)
        {
            query = query.Where(wait => wait.MandatoryPart == mandatoryPart);
        }
        return
            await query
            .OrderBy(x => x.IsFirst)
            .ToListAsync();
    }

    public async Task<List<MethodWaitEntity>> GetPendingWaitsForWorkflow(
        int rootWorkflowId,
        int methodGroupId,
        DateTime signalDate)
    {
        //todo: use this and delete `GetPendingWaitsForTemplate` and `_templatesRepo.GetWaitTemplatesForWorkflow`
        // load wait and `template.CallMandatoryPartPaths`
        var waits = await _context
          .MethodWaits
          .Where(
              wait =>
              wait.Status == WaitStatus.Waiting &&
              wait.TemplateId == rootWorkflowId &&
              wait.Created < signalDate)
          .ToListAsync();
        var templateIds = waits.Select(x => x.TemplateId);
        var templates = await _context.WaitTemplates.
            Where(x => templateIds.Contains(x.Id)).
            ToDictionaryAsync(x => x.Id, x => x);
        waits.ForEach(x => x.Template = templates[x.TemplateId]);
        return waits;
    }
}