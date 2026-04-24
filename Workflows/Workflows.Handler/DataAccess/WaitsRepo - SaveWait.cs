using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Workflows.Handler.Core;
using Workflows.Handler.Expressions;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Linq.Expressions;
using System.Reflection;

namespace Workflows.Handler.DataAccess;

internal partial class WaitsStore
{
    /// <summary>
    /// Add fresh from memory wait
    /// </summary>
    public async Task<bool> SaveWait(WaitEntity newWait)
    {
        _logger.LogInformation($"Starting SaveWait for {newWait.Name} requested by {newWait.RequestedByWorkflow}");

        if (newWait.ValidateWaitRequest() is false)
        {
            var message =
                $"Error when validate the requested wait [{newWait.Name}] " +
                $"that requested by workflow [{newWait.RequestedByWorkflow}].";
            _logger.LogError(message);
        }

        switch (newWait)
        {
            case MethodWaitEntity methodWait:
                _logger.LogInformation($"Saving MethodWaitEntity for {newWait.Name}");
                await SaveMethodWait(methodWait);
                break;
            case WaitsGroupEntity manyWaits:
                _logger.LogInformation($"Saving WaitsGroupEntity for {newWait.Name}");
                await SaveWaitsGroup(manyWaits);
                break;
            case WorkflowWaitEntity workflowWait:
                _logger.LogInformation($"Saving WorkflowWaitEntity for {newWait.Name}");
                await SaveWorkflowWait(workflowWait);
                break;
            case TimeWaitEntity timeWait:
                _logger.LogInformation($"Handling TimeWaitEntity for {newWait.Name}");
                await HandleTimeWaitRequest(timeWait);
                break;
        }

        _logger.LogInformation($"Finished SaveWait for {newWait.Name}");
        return false;
    }

    public async Task<MethodWaitEntity> GetMethodWait(long waitId, params Expression<Func<MethodWaitEntity, object>>[] includes)
    {
        _logger.LogInformation($"Getting MethodWaitEntity with ID {waitId}");
        var query = _context.MethodWaits.AsQueryable();
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        return await query
            .Where(x => x.Status == WaitStatus.Waiting)
            .FirstOrDefaultAsync(x => x.Id == waitId);
    }

    private async Task SaveMethodWait(MethodWaitEntity methodWait)
    {
        _logger.LogInformation($"Saving MethodWaitEntity for {methodWait.Name}");
        var methodId = await _methodIdsRepo.GetId(methodWait);
        var funcId = methodWait.RequestedByWorkflowId;
        var expressionsHash = new ExpressionsHashCalculator(methodWait.MatchExpression, methodWait.AfterMatchAction, methodWait.CancelMethodAction).HashValue;
        methodWait.MethodToWaitId = methodId.MethodId;
        methodWait.MethodGroupToWaitId = methodId.GroupId;

        await SetWaitTemplate();
        await AddWait(methodWait);

        async Task SetWaitTemplate()
        {
            WaitTemplate waitTemplate;
            if (methodWait.TemplateId == default)
            {
                waitTemplate =
                    await _waitTemplatesRepo.CheckTemplateExist(expressionsHash, funcId, methodId.GroupId) ??
                    await _waitTemplatesRepo.AddNewTemplate(expressionsHash, methodWait);
            }
            else
            {
                waitTemplate = await _waitTemplatesRepo.GetById(methodWait.TemplateId);
            }
            methodWait.TemplateId = waitTemplate.Id;
            methodWait.Template = waitTemplate;
        }
    }

    private async Task SaveWaitsGroup(WaitsGroupEntity waitsGroup)
    {
        _logger.LogInformation($"Saving WaitsGroupEntity for {waitsGroup.Name}");
        for (var index = 0; index < waitsGroup.ChildWaits.Count; index++)
        {
            var childWait = waitsGroup.ChildWaits[index];
            childWait.WorkflowInstance = waitsGroup.WorkflowInstance;
            childWait.RequestedByWorkflowId = waitsGroup.RequestedByWorkflowId;
            childWait.RequestedByWorkflow = waitsGroup.RequestedByWorkflow;
            childWait.StateAfterWait = waitsGroup.StateAfterWait;
            childWait.ParentWait = waitsGroup;
            childWait.CurrentWorkflow = waitsGroup.CurrentWorkflow;
            await SaveWait(childWait);
        }

        await AddWait(waitsGroup);
    }

    private async Task SaveWorkflowWait(WorkflowWaitEntity workflowWait)
    {
        _logger.LogInformation($"Saving WorkflowWaitEntity for {workflowWait.Name}");
        try
        {
            var workflowRunner =
                workflowWait.Runner != null ?
                new WorkflowRunner(workflowWait.Runner) :
                new WorkflowRunner(workflowWait.CurrentWorkflow, workflowWait.WorkflowInfo);
            var hasNext = await workflowRunner.MoveNextAsync();
            workflowWait.FirstWait = workflowRunner.CurrentWaitEntity;
            if (hasNext is false)
            {
                _logger.LogWarning($"No waits exist in sub workflow ({workflowWait.WorkflowInfo.GetFullName()})");
                return;
            }

            workflowWait.FirstWait = workflowRunner.CurrentWaitEntity;
            workflowWait.FirstWait.WorkflowInstance = workflowWait.WorkflowInstance;
            workflowWait.FirstWait.WorkflowInstanceId = workflowWait.WorkflowInstance.Id;
            workflowWait.FirstWait.ParentWait = workflowWait;
            workflowWait.FirstWait.ParentWaitId = workflowWait.Id;
            workflowWait.FirstWait.IsFirst = workflowWait.IsFirst;
            workflowWait.FirstWait.WasFirst = workflowWait.WasFirst;
            var methodId = await _workflowIdentifiersStore.GetWorkflowIdentifier(new MethodData(workflowWait.WorkflowInfo));
            workflowWait.FirstWait.RequestedByWorkflow = methodId;
            workflowWait.FirstWait.RequestedByWorkflowId = methodId.Id;

            await SaveWait(workflowWait.FirstWait);//first wait for sub workflow
            await AddWait(workflowWait);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error when saving workflow wait for {workflowWait.Name}");
            await _logsRepo.AddErrorLog(ex, "When save workflow wait", StatusCodes.WaitValidation);
        }
    }

    private async Task HandleTimeWaitRequest(TimeWaitEntity timeWait)
    {
        _logger.LogInformation($"Handling TimeWaitEntity for {timeWait.Name}");
        var timeWaitCallbackMethod = timeWait.TimeWaitMethod;

        var methodId = await _methodIdsRepo.GetId(timeWaitCallbackMethod);

        var timeWaitInput = new TimeWaitInput
        {
            TimeMatchId = timeWait.UniqueMatchId,
            RequestedByWorkflowId = timeWait.RequestedByWorkflowId,
            Description = $"[{timeWait.Name}] in workflow [{timeWait.RequestedByWorkflow.RF_MethodUrn}:{timeWait.WorkflowInstance.Id}]"
        };
        if (!timeWait.IgnoreJobCreation)
            timeWaitCallbackMethod.ExtraData.JobId = _backgroundJobClient.Schedule(
                () => new LocalRegisteredMethods().TimeWait(timeWaitInput), timeWait.TimeToWait);

        timeWaitCallbackMethod.MethodToWaitId = methodId.MethodId;
        timeWaitCallbackMethod.MethodGroupToWaitId = methodId.GroupId;

        await SaveMethodWait(timeWaitCallbackMethod);
        timeWaitCallbackMethod.MandatoryPart = timeWait.UniqueMatchId;
        _context.Entry(timeWait).State = EntityState.Detached;
    }

    /// <summary>
    /// Add waits from leefs to roots
    /// </summary>
    public Task AddWait(WaitEntity wait)
    {
        _logger.LogInformation($"Adding WaitEntity for {wait.Name}");
        var isTracked = _context.Waits.Local.Contains(wait);
        var isAddStatus = _context.Entry(wait).State == EntityState.Added;
        wait.OnAddWait();

        if (isTracked || isAddStatus) return Task.CompletedTask;

        _logger.LogInformation($"Add Wait [{wait.Name}] with type [{wait.WaitType}]");
        switch (wait)
        {
            case WaitsGroupEntity waitGroup:
                waitGroup.ChildWaits.RemoveAll(x => x is TimeWaitEntity);
                break;
            case MethodWaitEntity { MethodToWaitId: > 0 } methodWait:
                methodWait.MethodToWait = null;
                break;
        }

        _context.Waits.Add(wait);
        return Task.CompletedTask;
    }
}
