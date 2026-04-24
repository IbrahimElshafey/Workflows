using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.BaseUse;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Workflows.Handler.Core
{
    internal class WaitsProcessor : ISignalsProcessor
    {
        private readonly IFirstWaitProcessor _firstWaitProcessor;
        private readonly IWaitsStore _waitsRepo;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WaitsProcessor> _logger;
        private readonly IBackgroundProcess _backgroundJobClient;
        private readonly IUnitOfWork _context;
        private readonly BackgroundJobExecutor _backgroundJobExecutor;
        private readonly IDistributedLockProvider _lockProvider;
        private readonly ISignalWaitMatchStore _waitProcessingRecordsRepo;
        private readonly IMethodIdentifiersStore _methodIdsRepo;
        private readonly IPrivateDataStore _privateDataRepo;
        private readonly IWaitTemplatesStore _templatesRepo;
        private readonly ISignalsStore _signalsRepo;
        private readonly ILogsRepo _logsRepo;
        private readonly IWorkflowsSettings _settings;
        private SignalWaitMatch _waitCall;
        private MethodWaitEntity _methodWait;
        private SignalEntity _signal;

        public WaitsProcessor(
            IServiceProvider serviceProvider,
            ILogger<WaitsProcessor> logger,
            IFirstWaitProcessor firstWaitProcessor,
            IWaitsStore waitsRepo,
            IBackgroundProcess backgroundJobClient,
            IUnitOfWork context,
            BackgroundJobExecutor backgroundJobExecutor,
            IDistributedLockProvider lockProvider,
            ISignalWaitMatchStore waitsForCallsRepo,
            IMethodIdentifiersStore methodIdsRepo,
            IWaitTemplatesStore templatesRepo,
            ISignalsStore signalsRepo,
            ILogsRepo logsRepo,
            IWorkflowsSettings settings,
            IPrivateDataStore privateDataRepo)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _firstWaitProcessor = firstWaitProcessor;
            _waitsRepo = waitsRepo;
            _backgroundJobClient = backgroundJobClient;
            _context = context;
            _backgroundJobExecutor = backgroundJobExecutor;
            _lockProvider = lockProvider;
            _waitProcessingRecordsRepo = waitsForCallsRepo;
            _methodIdsRepo = methodIdsRepo;
            _templatesRepo = templatesRepo;
            _signalsRepo = signalsRepo;
            _logsRepo = logsRepo;
            _settings = settings;
            _privateDataRepo = privateDataRepo;
        }

        [DisplayName("Find Workflow Matched Waits [Workflow ID: {0}], [Pushed Call ID: {1}], [Method Group ID: {2}]")]
        public async Task ProcessSignalMatchesAsync(
            int workflowId,
            long signalId,
            int methodGroupId,
            DateTime signalDate)
        {
            await _backgroundJobExecutor.ExecuteWithLock(
                $"ProcessWorkflowExpectedMatchedWaits_{workflowId}_{signalId}",
                async () =>
                {
                    _signal = await LoadSignal(signalId);
                    var waitTemplates = await _templatesRepo.GetWaitTemplatesForWorkflow(methodGroupId, workflowId);
                    var matchExist = false;
                    if (waitTemplates == null)
                        return;
                    foreach (var template in waitTemplates)
                    {
                        var waits = await _waitsRepo.GetPendingWaitsForTemplate(
                            template.Id,
                            _signal.GetMandatoryPart(template.CallMandatoryPartPaths),
                            signalDate,
                            x => x.RequestedByWorkflow,
                            x => x.WorkflowInstance);

                        if (waits == null)
                            continue;
                        foreach (var wait in waits)
                        {
                            await LoadWaitProps(wait);
                            wait.Template = template;//why this line???
                            _waitCall =
                                 _waitProcessingRecordsRepo.Add(
                                    new SignalWaitMatch
                                    {
                                        WorkflowId = workflowId,
                                        SignalId = signalId,
                                        ServiceId = template.ServiceId,
                                        WaitId = wait.Id,
                                        StateId = wait.WorkflowInstanceId,
                                        TemplateId = template.Id
                                    });

                            _methodWait = wait;

                            var isSuccess = await Pipeline(
                                SetInputOutput,
                                CheckIfMatch,
                                CloneIfFirst,
                                ExecuteAfterMatchAction,
                                ResumeExecution);

                            await _context.CommitAsync();

                            if (!isSuccess) continue;

                            matchExist = true;
                            break;
                        }

                        if (matchExist) break;
                    }
                },
                $"Error when process wait [{_methodWait?.Id}] that may be a match for pushed call [{signalId}] and workflow [{workflowId}]");
        }

        private async Task LoadWaitProps(MethodWaitEntity methodWait)
        {
            methodWait.MethodToWait = await _methodIdsRepo.GetMethodIdentifierById(methodWait.MethodToWaitId);
            if (methodWait.ClosureDataId != null)
                methodWait.ClosureData = await _privateDataRepo.GetPrivateData(methodWait.ClosureDataId.Value);
            if (methodWait.LocalsId != null)
                methodWait.Locals = await _privateDataRepo.GetPrivateData(methodWait.LocalsId.Value);
            if (methodWait.MethodToWait == null)
            {
                var error = $"No method exist that linked to wait [{methodWait.MethodToWaitId}].";
                _logger.LogError(error);
                throw new Exception(error);
            }
            methodWait.WorkflowInstance.LoadUnmappedProps(methodWait.RequestedByWorkflow.InClassType);
            methodWait.LoadUnmappedProps();
        }

        private Task<bool> SetInputOutput()
        {
            _signal.LoadUnmappedProps(_methodWait.MethodToWait.MethodInfo);
            _methodWait.Input = _signal.Data.Input;
            _methodWait.Output = _signal.Data.Output;
            return Task.FromResult(true);
        }

        private async Task<bool> CheckIfMatch()
        {
            _methodWait.CurrentWorkflow.InitializeDependencies(_serviceProvider);
            var signalId = _signal.Id;
            try
            {
                var matched = _methodWait.IsMatched();
                if (matched is true)
                {
                    //todo:delete this and group waits by root workflow from the begining
                    var hasMatchBefore =
                      await _signalsRepo.IsSignalAlreadyMatchedToWorkflow(signalId, _methodWait.RootWorkflowId);
                    if (hasMatchBefore)
                    {
                        await _logsRepo.AddLog(
                            $"Pushed call [{signalId}] can't activate wait [{_methodWait.Name}]" +
                            $"because another wait for this instance activated before with same call ID.",
                            LogType.Warning,
                            StatusCodes.WaitProcessing);
                        UpdateWaitRecord(x => x.MatchStatus = MatchStatus.NotMatched);
                        return false;
                    }

                    var message =
                        $"Wait [{_methodWait.Name}] matched in [{_methodWait.RequestedByWorkflow.RF_MethodUrn}].";

                    if (_methodWait.IsFirst)
                        await _logsRepo.AddLog(message, LogType.Info, StatusCodes.WaitProcessing);
                    else
                        _methodWait.WorkflowInstance.AddLog(message, LogType.Info, StatusCodes.WaitProcessing);
                    UpdateWaitRecord(x => x.MatchStatus = MatchStatus.Matched);
                }

                if (matched is false)
                {
                    UpdateWaitRecord(x => x.MatchStatus = MatchStatus.NotMatched);
                }

                return matched;
            }
            catch (Exception ex)
            {
                var error =
                    $"Error occurred when evaluate match for [{_methodWait.Name}] " +
                    $"in [{_methodWait.RequestedByWorkflow.RF_MethodUrn}] when pushed call [{signalId}].";
                if (_methodWait.IsFirst)
                {
                    _methodWait.WorkflowInstance.Status = WorkflowInstanceStatus.InError;
                    await _logsRepo.AddErrorLog(ex, error, StatusCodes.WaitProcessing);
                }
                else
                {
                    _methodWait.Status = _settings.WaitStatusIfProcessingError;
                    _methodWait.WorkflowInstance.Status = WorkflowInstanceStatus.InError;
                    _methodWait.WorkflowInstance.AddError(error, StatusCodes.WaitProcessing, ex);
                }
                await _methodWait.CurrentWorkflow?.OnError(error, ex);
                throw new Exception(error, ex);
            }
        }

        private async Task<bool> CloneIfFirst()
        {
            if (_methodWait.IsFirst)
            {
                _methodWait = await _firstWaitProcessor.DuplicateFirstWait(_methodWait);
                _waitCall.StateId = _methodWait.WorkflowInstanceId;
                _waitCall.WaitId = _methodWait.Id;
                _methodWait.WorkflowInstance.Status = WorkflowInstanceStatus.InProgress;
                await _context.CommitAsync();
            }
            return true;
        }


        private async Task<bool> ExecuteAfterMatchAction()
        {

            var signalId = _signal.Id;
            _methodWait.SignalId = signalId;
            try
            {
                await using (await _lockProvider.AcquireLockAsync($"{_settings.CurrentWaitsDbName}_UpdateWorkflowInstance_{_methodWait.WorkflowInstanceId}"))
                {
                    if (_methodWait.ExecuteAfterMatchAction())
                    {
                        _context.MarkEntityAsModified(_methodWait.WorkflowInstance);
                        if (_methodWait.ClosureData != null)
                            _context.MarkEntityAsModified(_methodWait.ClosureData);
                        await _context.CommitAsync();//Review: why?
                        UpdateWaitRecord(x => x.AfterMatchActionStatus = ExecutionStatus.ExecutionSucceeded);

                    }
                    else
                    {
                        _methodWait.Status = _settings.WaitStatusIfProcessingError;
                        _methodWait.WorkflowInstance.Status = WorkflowInstanceStatus.InError;
                        UpdateWaitRecord(x => x.AfterMatchActionStatus = ExecutionStatus.ExecutionFailed);
                        return false;
                    }
                }
                _methodWait.CurrentWorkflow.InitializeDependencies(_serviceProvider);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _methodWait.WorkflowInstance.AddError(
                    $"Concurrency Exception occurred when process wait [{_methodWait.Name}]." +
                    $"\nProcessing this wait will be scheduled.",
                    StatusCodes.WaitProcessing, ex);

                _backgroundJobClient.Schedule(() =>
                        ProcessSignalMatchesAsync(_methodWait.RequestedByWorkflowId, signalId, _methodWait.MethodGroupToWaitId, _signal.Created),
                    TimeSpan.FromSeconds(10));
                return false;
            }
            catch (Exception ex)
            {
                await _methodWait.CurrentWorkflow?.OnError("Error when execute after match action.", ex);
                return false;
            }

            return true;
        }

        private async Task<bool> ResumeExecution()
        {
            try
            {
                WaitEntity currentWait = _methodWait;
                do
                {
                    var parent = await _waitsRepo.GetWaitParent(currentWait);
                    switch (currentWait)
                    {
                        case MethodWaitEntity methodWait:
                            currentWait.Status = WaitStatus.Completed;
                            await TryProceedExecution(parent, methodWait);
                            await _context.CommitAsync();
                            if (parent != null)
                                parent.CurrentWorkflow = methodWait.CurrentWorkflow;
                            break;

                        case WaitsGroupEntity:
                        case WorkflowWaitEntity:
                            if (currentWait.IsCompleted())
                            {
                                currentWait.WorkflowInstance.AddLog($"Wait [{currentWait.Name}] is completed.", LogType.Info, StatusCodes.WaitProcessing);
                                currentWait.Status = WaitStatus.Completed;
                                await _waitsRepo.CancelSubWaits(currentWait.Id, _signal.Id);
                                if (currentWait.ClosureData != null)
                                    _context.MarkEntityAsModified(currentWait.ClosureData);
                                await TryProceedExecution(parent, currentWait);
                            }
                            else
                            {
                                UpdateWaitRecord(x => x.ExecutionStatus = ExecutionStatus.ExecutionSucceeded);
                                return true;
                            }
                            break;
                    }

                    currentWait = parent;

                } while (currentWait != null);

            }
            catch (Exception ex)
            {
                var errorMsg = $"Exception occurred when try to resume execution after [{_methodWait.Name}].";
                _methodWait.WorkflowInstance.AddError(errorMsg, StatusCodes.WaitProcessing, ex);
                _methodWait.WorkflowInstance.Status = WorkflowInstanceStatus.InError;
                UpdateWaitRecord(x => x.ExecutionStatus = ExecutionStatus.ExecutionFailed);
                await _methodWait.CurrentWorkflow?.OnError(errorMsg, ex);
                return false;
            }
            UpdateWaitRecord(x => x.ExecutionStatus = ExecutionStatus.ExecutionSucceeded);
            return true;
        }

        private async Task TryProceedExecution(WaitEntity parent, WaitEntity currentWait)
        {
            switch (parent)
            {
                case null:
                case WorkflowWaitEntity:
                    await ProceedToNextWait(currentWait);
                    break;
                case WaitsGroupEntity:
                    parent.WorkflowInstance.AddLog($"Wait group ({parent.Name}) to complete.", LogType.Info, StatusCodes.WaitProcessing);
                    currentWait.WorkflowInstance.Status = WorkflowInstanceStatus.InProgress;
                    break;
            }
        }

        private async Task ProceedToNextWait(WaitEntity currentWait)
        {
            try
            {
                if (currentWait.ParentWait != null && currentWait.ParentWait.Status != WaitStatus.Waiting)
                {
                    var errorMsg = $"Can't proceed to next ,Parent wait [{currentWait.ParentWait.Name}] status is not (Waiting).";
                    _logger.LogWarning(errorMsg);
                    currentWait.WorkflowInstance.AddError(errorMsg, StatusCodes.WaitProcessing, null);
                    return;
                }

                currentWait.Status = WaitStatus.Completed;
                var nextWait = await currentWait.GetNextWait();
                if (nextWait == null)
                {
                    if (currentWait.ParentWaitId == null)
                        await FinalExit(currentWait);
                    return;
                }
                nextWait.WorkflowInstance.Status = WorkflowInstanceStatus.InProgress;
                _context.MarkEntityAsModified(nextWait.WorkflowInstance);
                await SaveNewWait(nextWait);

            }
            catch (Exception ex)
            {
                var errorMessage = $"Error when proceed to next wait after {currentWait}";
                _logger.LogError(ex, errorMessage);
                //currentWait.WorkflowInstance.AddError(errorMessage, StatusCodes.WaitProcessing, ex);
                throw;
            }
        }

        private async Task SaveNewWait(WaitEntity nextWait)
        {
            await _waitsRepo.SaveWait(nextWait);
            await _context.CommitAsync();
        }

        private async Task FinalExit(WaitEntity currentWait)
        {
            _logger.LogInformation($"Final exit for workflow instance [{currentWait.WorkflowInstanceId}]");
            currentWait.Status = WaitStatus.Completed;
            currentWait.WorkflowInstance.StateObject = currentWait.CurrentWorkflow;
            currentWait.WorkflowInstance.AddLog("Workflow instance completed.", LogType.Info, StatusCodes.WaitProcessing);
            currentWait.WorkflowInstance.Status = WorkflowInstanceStatus.Completed;
            await _waitsRepo.CancelOpenedWaitsForState(currentWait.WorkflowInstanceId);//for confirmation calls
            await _logsRepo.ClearErrorsForWorkflowInstance(currentWait.WorkflowInstanceId);//for confirmation calls
            await currentWait.CurrentWorkflow?.OnCompleted();
        }

        private async Task<MethodWaitEntity> LoadWait(int waitId)
        {
            var methodWait = await _waitsRepo.GetMethodWait(waitId, x => x.RequestedByWorkflow, x => x.WorkflowInstance);

            if (methodWait == null)
            {
                var error = $"No method wait exist with ID ({waitId}) and status ({WaitStatus.Waiting}).";
                _logger.LogError(error);
                throw new Exception(error);
            }

            methodWait.MethodToWait = await _methodIdsRepo.GetMethodIdentifierById(methodWait.MethodToWaitId);

            if (methodWait.MethodToWait == null)
            {
                var error = $"No method exist that linked to wait [{waitId}].";
                _logger.LogError(error);
                throw new Exception(error);
            }

            methodWait.Template = await _templatesRepo.GetWaitTemplateWithBasicMatch(methodWait.TemplateId);
            if (methodWait.Template == null)
            {
                var error = $"No wait template exist for wait [{waitId}].";
                _logger.LogError(error);
                throw new Exception(error);
            }

            methodWait.WorkflowInstance.LoadUnmappedProps(methodWait.RequestedByWorkflow.InClassType);
            methodWait.LoadUnmappedProps();
            return methodWait;
        }

        private async Task<SignalEntity> LoadSignal(long signalId)
        {
            try
            {
                var signal = await _signalsRepo.GetSignal(signalId);

                if (signal != null) return signal;

                var error = $"No pushed method exist with ID ({signalId}).";
                _logger.LogError(error);
                throw new Exception(error);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error when process pushed method [{signalId}] and wait [{_methodWait.Id}].", ex);
            }
        }
        private void UpdateWaitRecord(Action<SignalWaitMatch> action, [CallerMemberName] string calledBy = "")
        {
            try
            {
                action(_waitCall);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"Failed to execute update wait record when [signalId:{_signal.Id}, waitId:{_methodWait.Id}, CalledBy:{calledBy}]");
            }
        }

        private async Task<bool> Pipeline(params Func<Task<bool>>[] actions)
        {
            foreach (var action in actions)
                if (!await action())
                    return false;
            return true;
        }
    }


}
