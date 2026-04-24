using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Reflection;

namespace Workflows.Handler.DataAccess;
internal class MethodIdentifiersStore : IMethodIdentifiersStore, IWorkflowIdentifiersStore
{
    private readonly ILogger<MethodIdentifiersStore> _logger;
    private readonly WaitsDataContext _context;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IWorkflowsSettings _settings;

    public MethodIdentifiersStore(
        ILogger<MethodIdentifiersStore> logger,
        WaitsDataContext context,
        IDistributedLockProvider lockProvider,
        IWorkflowsSettings settings)
    {
        _logger = logger;
        _context = context;
        _lockProvider = lockProvider;
        _settings = settings;
    }

    public async Task<WorkflowIdentifier> GetWorkflowIdentifier(int id)
    {
        var resumableWorkflowIdentifier =
           await _context
               .WorkflowIdentifiers
               .FirstOrDefaultAsync(x => x.Id == id);
        if (resumableWorkflowIdentifier != null)
            return resumableWorkflowIdentifier;
        var error = $"Can't find resumable workflow with ID [{id}] in database.";
        _logger.LogError(error);
        throw new NullReferenceException(error);
    }

    public async Task<WorkflowIdentifier> TryGetWorkflow(MethodData methodData)
    {
        methodData.Validate();
        return await _context
                .WorkflowIdentifiers
                .FirstOrDefaultAsync(
                    x => (x.Type == MethodType.WorkflowEntryPoint || x.Type == MethodType.SubWorkflow) &&
                         x.RF_MethodUrn == methodData.MethodUrn &&
                         x.ServiceId == _settings.CurrentServiceId);
    }

    public async Task<WorkflowIdentifier> GetWorkflowIdentifier(MethodData methodData)
    {
        var resumableWorkflowIdentifier = await TryGetWorkflow(methodData);
        if (resumableWorkflowIdentifier != null)
            return resumableWorkflowIdentifier;
        var error = $"Can't find resumable workflow ({methodData}) in database.";
        _logger.LogError(error);
        throw new NullReferenceException(error);
    }

    public async Task<WorkflowIdentifier> AddWorkflowIdentifier(MethodData methodData)
    {
        await using var lockHandle =
            await _lockProvider.AcquireLockAsync($"{_settings.CurrentWaitsDbName}_AddWorkflowIdentifier_{methodData.MethodUrn}");
        var inDb = await TryGetWorkflow(methodData);
        if (inDb != null)
        {
            inDb.FillFromMethodData(methodData);
            return inDb;
        }

        var add = new WorkflowIdentifier();
        add.FillFromMethodData(methodData);
        _context.WorkflowIdentifiers.Add(add);
        return add;
    }

    public async Task AddMethodIdentifier(MethodData methodData)
    {
        await using var waitHandle =
            await _lockProvider.AcquireLockAsync($"{_settings.CurrentWaitsDbName}_AddMethodIdentifier_{methodData.MethodUrn}");
        var methodGroup =
            await _context
                .MethodsGroups
                .Include(x => x.WaitMethodIdentifiers)
                .FirstOrDefaultAsync(x => x.MethodGroupUrn == methodData.MethodUrn);
        var methodInDb = methodGroup?.WaitMethodIdentifiers?
            .FirstOrDefault(x =>
            x.MethodHash.SequenceEqual(methodData.MethodHash) &&
            x.ServiceId == _settings.CurrentServiceId);

        var isUpdate =
            methodGroup != null &&
            methodInDb != null;
        if (isUpdate)
        {
            methodInDb.FillFromMethodData(methodData);
            return;
        }

        var toAdd = new MethodIdentifier();
        toAdd.FillFromMethodData(methodData);

        var isChildAdd = methodGroup != null;
        if (isChildAdd)
            AddMethodIdToGroup(methodData, methodGroup, toAdd);
        else
            await CreateNewMethodGroup(methodData, toAdd);


    }

    private async Task CreateNewMethodGroup(MethodData methodData, MethodIdentifier toAdd)
    {
        var group = new MethodsGroup
        {
            MethodGroupUrn = methodData.MethodUrn,
            CanPublishFromExternal = methodData.CanPublishFromExternal,
            IsLocalOnly = methodData.IsLocalOnly,
        };
        group.WaitMethodIdentifiers.Add(toAdd);
        _context.MethodsGroups.Add(group);
        await _context.SaveChangesAsync();
    }

    private static void AddMethodIdToGroup(MethodData methodData, MethodsGroup methodGroup, MethodIdentifier toAdd)
    {
        //todo: how the user can change IsLocalOnly and CanPublishFromExternal
        if (methodGroup.IsLocalOnly != methodData.IsLocalOnly)
            throw new Exception(ErrorTemplate(nameof(MethodsGroup.IsLocalOnly), methodGroup.IsLocalOnly));

        if (methodGroup.CanPublishFromExternal != methodData.CanPublishFromExternal)
            throw new Exception(ErrorTemplate(nameof(MethodsGroup.CanPublishFromExternal),
                methodGroup.CanPublishFromExternal));

        methodGroup.WaitMethodIdentifiers?.Add(toAdd);

        string ErrorTemplate(string propName, bool propValue) =>
           $"Error When register method {methodData.MethodName}," +
           $"Method group [{methodGroup.MethodGroupUrn}] property [{propName}] was [{propValue}] and can't be changed";
    }


    public async Task<(int MethodId, int GroupId)> GetId(MethodWaitEntity methodWait)
    {
        if (methodWait.MethodGroupToWaitId != default && methodWait.MethodToWaitId != default)
            return (methodWait.MethodToWaitId ?? 0, methodWait.MethodGroupToWaitId);

        var methodData = methodWait.MethodData;
        methodData.Validate();
        var groupIdQuery = _context
            .MethodsGroups
            .Where(x => x.MethodGroupUrn == methodData.MethodUrn)
            .Select(x => x.Id);

        var methodIdQry = _context
            .WaitMethodIdentifiers
            .Where(x =>
                groupIdQuery.Contains(x.MethodGroupId) &&
                x.MethodName == methodData.MethodName &&
                x.ClassName == methodData.ClassName &&
                x.AssemblyName == methodData.AssemblyName
            )
            .Select(x => new { x.Id, x.MethodGroupId });
        var methodId = await methodIdQry.FirstAsync();
        return (methodId.Id, methodId.MethodGroupId);
    }

    public async Task<MethodIdentifier> GetMethodIdentifierById(int? methodToWaitId)
    {
        return
            await _context
            .WaitMethodIdentifiers
            .FindAsync(methodToWaitId);
    }

    public async Task<bool> CanPublishFromExternal(string methodUrn)
    {
        return await _context
             .MethodsGroups
             .Where(x => x.MethodGroupUrn == methodUrn)
             .AnyAsync(x => x.CanPublishFromExternal);
    }
}