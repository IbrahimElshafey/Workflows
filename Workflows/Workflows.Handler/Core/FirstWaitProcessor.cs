using Microsoft.Extensions.Logging;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.ComponentModel;
using System.Reflection;

namespace Workflows.Handler.Core;

internal class FirstWaitProcessor : IFirstWaitProcessor
{
    private readonly ILogger<FirstWaitProcessor> _logger;
    private readonly IUnitOfWork _context;
    private readonly IMethodIdentifiersStore _methodIdentifierRepo;
    private readonly IWorkflowIdentifiersStore _workflowIdentifiersStore;
    private readonly IWaitsStore _waitsRepository;
    private readonly IWaitTemplatesStore _templatesRepo;
    private readonly IServiceProvider _serviceProvider;
    private readonly BackgroundJobExecutor _backgroundJobExecutor;
    private readonly IBackgroundProcess _backgroundJobClient;
    private readonly ILogsRepo _logsRepo;
    private readonly IScanLocksRepo _scanStateRepo;

    public FirstWaitProcessor(
        ILogger<FirstWaitProcessor> logger,
        IUnitOfWork context,
        IServiceProvider serviceProvider,
        IMethodIdentifiersStore methodIdentifierRepo,
        IWaitsStore waitsRepository,
        BackgroundJobExecutor backgroundJobExecutor,
        IBackgroundProcess backgroundJobClient,
        ILogsRepo logsRepo,
        IWaitTemplatesStore templatesRepo,
        IScanLocksRepo scanStateRepo,
        IWorkflowIdentifiersStore workflowIdentifiersStore)
    {
        _logger = logger;
        _context = context;
        _serviceProvider = serviceProvider;
        _methodIdentifierRepo = methodIdentifierRepo;
        _waitsRepository = waitsRepository;
        _backgroundJobExecutor = backgroundJobExecutor;
        _backgroundJobClient = backgroundJobClient;
        _logsRepo = logsRepo;
        _templatesRepo = templatesRepo;
        _scanStateRepo = scanStateRepo;
        _workflowIdentifiersStore = workflowIdentifiersStore;
    }

    public async Task<MethodWaitEntity> DuplicateFirstWait(MethodWaitEntity firstMatchedMethodWait)
    {
        WorkflowIdentifier resumableWorkflow = null;
        try
        {
            resumableWorkflow = await _workflowIdentifiersStore.GetWorkflowIdentifier(firstMatchedMethodWait.RootWorkflowId);
            var firstWaitClone = await GetFirstWait(resumableWorkflow.MethodInfo, false);
            firstWaitClone.ActionOnChildrenTree(waitClone =>
            {
                waitClone.Status = WaitStatus.Temp;
                waitClone.IsFirst = false;
                waitClone.WasFirst = true;
                waitClone.WorkflowInstance.StateObject = firstMatchedMethodWait?.WorkflowInstance?.StateObject;
                if (waitClone is TimeWaitEntity timeWait)
                {
                    timeWait.TimeWaitMethod.ExtraData.JobId = _backgroundJobClient.Schedule(
                        () => new LocalRegisteredMethods().TimeWait(
                        new TimeWaitInput
                        {
                            TimeMatchId = firstMatchedMethodWait.MandatoryPart,
                            RequestedByWorkflowId = firstMatchedMethodWait.RequestedByWorkflowId,
                            Description = $"[{timeWait.Name}] in workflow [{firstMatchedMethodWait.RequestedByWorkflow.RF_MethodUrn}:{firstMatchedMethodWait.WorkflowInstance.Id}]"
                        }), timeWait.TimeToWait);
                    timeWait.TimeWaitMethod.MandatoryPart = firstMatchedMethodWait.MandatoryPart;
                    timeWait.IgnoreJobCreation = true;
                }

            });

            firstWaitClone.WorkflowInstance.Logs.AddRange(firstWaitClone.WorkflowInstance.Logs);
            firstWaitClone.WorkflowInstance.Status =
                firstWaitClone.WorkflowInstance.HasErrors() ?
                WorkflowInstanceStatus.InError :
                WorkflowInstanceStatus.InProgress;
            await _waitsRepository.SaveWait(firstWaitClone);//first wait clone

            //return method wait that 
            var currentMatchedMw = firstWaitClone.GetChildMethodWait(firstMatchedMethodWait.Name);
            currentMatchedMw.Input = firstMatchedMethodWait.Input;
            currentMatchedMw.Output = firstMatchedMethodWait.Output;
            var waitTemplate = await _templatesRepo.GetWaitTemplateWithBasicMatch(firstMatchedMethodWait.TemplateId);
            currentMatchedMw.TemplateId = waitTemplate.Id;
            currentMatchedMw.Template = waitTemplate;
            currentMatchedMw.IsFirst = false;
            currentMatchedMw.LoadExpressions();
            await _context.CommitAsync();
            firstWaitClone.ActionOnChildrenTree(waitClone => waitClone.Status = WaitStatus.Waiting);
            return currentMatchedMw;
        }
        catch (Exception ex)
        {
            var error = $"Error when try to clone first wait for workflow [{resumableWorkflow?.RF_MethodUrn}]";
            await _logsRepo.AddErrorLog(ex, error, StatusCodes.FirstWait);
            throw new Exception(error, ex);
        }
    }

    [DisplayName("Register First Wait for Workflow [{0},{1}]")]
    public async Task RegisterFirstWait(int workflowId, string methodUrn)
    {
        MethodInfo resumableWorkflow = null;
        var workflowName = "";
        string firstWaitLock = $"FirstWaitProcessor_RegisterFirstWait_{workflowId}";
        int firstWaitLockId = -1;
        firstWaitLockId = await _scanStateRepo.AddLock(firstWaitLock);
        try
        {
            await _backgroundJobExecutor.ExecuteWithLock(
            $"FirstWaitProcessor_RegisterFirstWait_{workflowId}",//may many services instances
            async () =>
            {
                try
                {
                    var resumableWorkflowId = await _workflowIdentifiersStore.GetWorkflowIdentifier(workflowId);
                    methodUrn = resumableWorkflowId.RF_MethodUrn;
                    resumableWorkflow = resumableWorkflowId.MethodInfo;
                    workflowName = resumableWorkflow.Name;
                    _logger.LogInformation($"Trying Start Resumable Workflow [{resumableWorkflowId.RF_MethodUrn}] And Register First Wait");
                    var firstWait = await GetFirstWait(resumableWorkflow, true);

                    if (firstWait != null)
                    {
                        await _logsRepo.AddLog(
                            $"[{resumableWorkflow.GetFullName()}] started and wait [{firstWait.Name}] to match.", LogType.Info, StatusCodes.FirstWait);

                        await _waitsRepository.SaveWait(firstWait);
                        _logger.LogInformation(
                            $"SaveSignal first wait [{firstWait.Name}] for workflow [{resumableWorkflow.GetFullName()}].");
                        await _context.CommitAsync();
                    }
                }
                catch (Exception ex)
                {
                    if (resumableWorkflow != null)
                        await _logsRepo.AddErrorLog(ex, ErrorMsg(), StatusCodes.FirstWait);

                    await _waitsRepository.RemoveFirstWaitIfExist(workflowId);
                    throw;
                }

            },
            ErrorMsg());
        }
        finally
        {
            if (firstWaitLockId > -1)
                await _scanStateRepo.RemoveLock(firstWaitLockId);
        }
        string ErrorMsg() => $"Error when try to register first wait for workflow [{workflowName}:{workflowId}]";
    }


    public async Task<WaitEntity> GetFirstWait(MethodInfo resumableWorkflow, bool removeIfExist)
    {
        try
        {
            //todo: WorkflowContainer must be constructor less if you want to pass dependancies create a method `SetDependencies`
            var classInstance = (WorkflowContainer)Activator.CreateInstance(resumableWorkflow.DeclaringType);

            if (classInstance == null)
            {
                var errorMsg = $"Can't initiate a new instance of [{resumableWorkflow.DeclaringType.FullName}]";
                await _logsRepo.AddErrorLog(null, errorMsg, StatusCodes.FirstWait);

                throw new NullReferenceException(errorMsg);
            }

            classInstance.InitializeDependencies(_serviceProvider);
            classInstance.CurrentWorkflow = resumableWorkflow;
            var workflowRunner = new WorkflowRunner(classInstance, resumableWorkflow);
            //if (workflowRunner.WorkflowExistInCode is false)
            //{
            //    var message = $"Resumable workflow ({resumableWorkflow.GetFullName()}) not exist in code.";
            //    _logger.LogWarning(message);
            //    await _logsRepo.AddErrorLog(null, message, StatusCodes.FirstWait);
            //    throw new NullReferenceException(message);
            //}

            await workflowRunner.MoveNextAsync();
            var firstWait = workflowRunner.CurrentWaitEntity;

            if (firstWait == null)
            {
                await _logsRepo.AddErrorLog(
                    null,
                    $"Can't get first wait in workflow [{resumableWorkflow.GetFullName()}].",
                    StatusCodes.FirstWait);
                return null;
            }

            var workflowId = await _workflowIdentifiersStore.GetWorkflowIdentifier(new MethodData(resumableWorkflow));
            if (removeIfExist)
            {
                _logger.LogInformation("First wait already exist it will be deleted and recreated since it may be changed.");
                await _waitsRepository.RemoveFirstWaitIfExist(workflowId.Id);
            }
            var workflowInstance = new WorkflowInstance
            {
                WorkflowIdentifier = workflowId,
                StateObject = classInstance,
            };
            firstWait.ActionOnChildrenTree(x =>
            {
                x.RequestedByWorkflow = workflowId;
                x.RequestedByWorkflowId = workflowId.Id;
                x.IsFirst = true;
                x.WasFirst = true;
                x.RootWorkflowId = workflowId.Id;
                x.WorkflowInstance = workflowInstance;
            });
            return firstWait;
        }
        catch (Exception ex)
        {
            await _logsRepo.AddErrorLog(ex, "Error when get first wait.", StatusCodes.FirstWait);
            throw;
        }
    }
}