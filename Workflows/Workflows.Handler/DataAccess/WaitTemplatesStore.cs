using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;

namespace Workflows.Handler.DataAccess;

internal class WaitTemplatesStore : IWaitTemplatesStore
{
    private readonly WaitsDataContext _context;
    private readonly IWorkflowsSettings _settings;
    private IServiceProvider _serviceProvider;

    public WaitTemplatesStore(IServiceProvider serviceProvider, WaitsDataContext context, IWorkflowsSettings settings)
    {
        _settings = settings;
        _context = context;
        _serviceProvider = serviceProvider;
        _serviceProvider.GetService<WaitsDataContext>();
    }

    public async Task<WaitTemplate> AddNewTemplate(byte[] hashResult, MethodWaitEntity methodWait)
    {
        return await AddNewTemplate(
            hashResult,
            methodWait.CurrentWorkflow,
            methodWait.RequestedByWorkflowId,
            methodWait.MethodGroupToWaitId,
            methodWait.MethodToWaitId,
            methodWait.InCodeLine,
            methodWait.CancelMethodAction,
            methodWait.AfterMatchAction,
            methodWait.MatchExpressionParts
            );
    }

    public async Task<WaitTemplate> CheckTemplateExist(byte[] hash, int funcId, int groupId)
    {
        var waitTemplate = (await
            _context.WaitTemplates
            .Where(x =>
                x.MethodGroupId == groupId &&
                x.WorkflowId == funcId &&
                x.ServiceId == _settings.CurrentServiceId)
            .ToListAsync())
            .FirstOrDefault(x => x.Hash.SequenceEqual(hash));
        if (waitTemplate != null)
        {
            waitTemplate.LoadUnmappedProps();
            if (waitTemplate.IsActive == -1)
            {
                waitTemplate.IsActive = 1;
                await _context.SaveChangesDirectly();
            }
        }
        return waitTemplate;
    }


    public async Task<List<WaitTemplate>> GetWaitTemplatesForWorkflow(int methodGroupId, int workflowId)
    {
        var waitTemplatesQry = _context
            .WaitTemplates
            .Where(template =>
                template.WorkflowId == workflowId &&
                template.MethodGroupId == methodGroupId &&
                template.ServiceId == _settings.CurrentServiceId &&
                template.IsActive == 1);

        var result = await
            waitTemplatesQry
            .OrderByDescending(x => x.Id)
            .AsNoTracking()
            .ToListAsync();

        result.ForEach(x => x.LoadUnmappedProps());
        return result;
    }


    public async Task<WaitTemplate> GetById(int templateId)
    {
        var waitTemplate = await _context.WaitTemplates.FindAsync(templateId);
        waitTemplate?.LoadUnmappedProps();
        return waitTemplate;
    }

    public async Task<WaitTemplate> GetWaitTemplateWithBasicMatch(int methodWaitTemplateId)
    {
        var template =
            await _context
            .WaitTemplates
            .Select(waitTemplate =>
                new WaitTemplate
                {
                    MatchExpressionValue = waitTemplate.MatchExpressionValue,
                    AfterMatchAction = waitTemplate.AfterMatchAction,
                    Id = waitTemplate.Id,
                    WorkflowId = waitTemplate.WorkflowId,
                    MethodId = waitTemplate.MethodId,
                    MethodGroupId = waitTemplate.MethodGroupId,
                    ServiceId = waitTemplate.ServiceId,
                    IsActive = waitTemplate.IsActive,
                    CancelMethodAction = waitTemplate.CancelMethodAction,
                })
            .FirstAsync(x => x.Id == methodWaitTemplateId);
        template.LoadUnmappedProps();
        return template;
    }

    public async Task<WaitTemplate> AddNewTemplate(
        byte[] hashResult,
        object currentWorkflowInstance,
        int workflowId,
        int groupId,
        int? methodId,
        int inCodeLine,
        string cancelMethodAction,
        string afterMatchAction,
        MatchExpressionParts matchExpressionParts
        )
    {
        var scope = _serviceProvider.CreateScope();
        var tempContext = scope.ServiceProvider.GetService<WaitsDataContext>();
        var waitTemplate = new WaitTemplate
        {
            MethodId = methodId,
            WorkflowId = workflowId,
            MethodGroupId = groupId,
            Hash = hashResult,
            InCodeLine = inCodeLine,
            IsActive = 1,
            CancelMethodAction = cancelMethodAction,
            AfterMatchAction = afterMatchAction
        };

        if (matchExpressionParts != null)
        {
            waitTemplate.MatchExpression = matchExpressionParts.MatchExpression;
            waitTemplate.CallMandatoryPartPaths = matchExpressionParts.CallMandatoryPartPaths;
            waitTemplate.InstanceMandatoryPartExpression = matchExpressionParts.InstanceMandatoryPartExpression;
            waitTemplate.IsMandatoryPartFullMatch = matchExpressionParts.IsMandatoryPartFullMatch;
        }

        tempContext.WaitTemplates.Add(waitTemplate);

        await tempContext.SaveChangesAsync();
        //reattach to current context
        _context.Attach(waitTemplate);
        return waitTemplate;
    }
}
