using Microsoft.Extensions.Logging;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Workflows.Handler.Core;
internal class ServiceQueue : IServiceQueue
{
    private readonly IBackgroundProcess _backgroundJobClient;
    private readonly ILogger<ServiceQueue> _logger;
    private readonly ISignalsProcessor _signalsProcessor;
    private readonly IWaitsStore _waitsRepository;
    private readonly BackgroundJobExecutor _backgroundJobExecutor;
    private readonly IWorkflowsSettings _settings;
    private readonly IScanLocksRepo _lockStateRepo;
    private readonly IHttpClientFactory _httpClientFactory;

    public ServiceQueue(
        ILogger<ServiceQueue> logger,
        ISignalsProcessor waitsProcessor,
        IWaitsStore waitsRepository,
        IBackgroundProcess backgroundJobClient,
        BackgroundJobExecutor backgroundJobExecutor,
        IWorkflowsSettings settings,
        IScanLocksRepo lockStateRepo,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _signalsProcessor = waitsProcessor;
        _waitsRepository = waitsRepository;
        _backgroundJobClient = backgroundJobClient;
        _backgroundJobExecutor = backgroundJobExecutor;
        _settings = settings;
        _lockStateRepo = lockStateRepo;
        _httpClientFactory = httpClientFactory;
    }

    [DisplayName("Identify Impacted Services [SignalId: {0},MethodUrn: {2}]")]
    public async Task IdentifyAffectedWorkflows(long signalId, DateTime puhsedSignalDate, string methodUrn)
    {
        //if scan is running schedule it for later processing
        if (!await _lockStateRepo.AreLocksExist())
        {
            //get current job id?
            _backgroundJobClient.Schedule(() => IdentifyAffectedWorkflows(
                signalId,
                puhsedSignalDate,
                methodUrn), TimeSpan.FromSeconds(3));
            return;
        }

        //no chance to be called by two services in same time, lock removed
        await _backgroundJobExecutor.ExecuteWithoutLock(
            async () =>
            {
                var impactedWorkflowsIds = await _waitsRepository.GetImpactedWorkflows(methodUrn, puhsedSignalDate);
                if (impactedWorkflowsIds == null || impactedWorkflowsIds.Any() is false)
                {
                    _logger.LogWarning($"There are no workflows affected by pushed signal [{methodUrn}:{signalId}]");
                    return;
                }

                foreach (var signalEffection in impactedWorkflowsIds)
                {
                    signalEffection.SignalId = signalId;
                    signalEffection.MethodUrn = methodUrn;
                    signalEffection.SignalDate = puhsedSignalDate;
                    var isLocal = signalEffection.AffectedServiceId == _settings.CurrentServiceId;
                    if (isLocal)
                        await EnqueueEffectionPerWorkflow(signalEffection);
                    else
                        await SendSignalToAnotherWorkflowService(signalEffection);
                }
            },
            $"Error when call [{nameof(IdentifyAffectedWorkflows)}(signalId:{signalId}, methodUrn:{methodUrn})] in service [{_settings.CurrentServiceId}]");
    }

    [DisplayName("Process call [Id: {0},MethodUrn: {1}] Locally.")]
    public async Task ProcessSignalLocally(long signalId, string methodUrn, DateTime puhsedSignalDate)
    {
        if (!await _lockStateRepo.AreLocksExist())
        {
            _backgroundJobClient.Schedule(() =>
            ProcessSignalLocally(signalId, methodUrn, puhsedSignalDate), TimeSpan.FromSeconds(3));
            return;
        }

        //$"{nameof(ProcessCallLocally)}_{signalId}_{_settings.CurrentServiceId}",
        //no chance to be called by two services at same time
        await _backgroundJobExecutor.ExecuteWithoutLock(
            async () =>
            {
                var callEffection = await _waitsRepository.GetSignalEffectionInCurrentService(methodUrn, puhsedSignalDate);

                if (callEffection != null)
                {
                    callEffection.SignalId = signalId;
                    callEffection.MethodUrn = methodUrn;
                    callEffection.SignalDate = puhsedSignalDate;
                    await EnqueueEffectionPerWorkflow(callEffection);
                }
                else
                {
                    _logger.LogWarning($"There are no workflows affected in current service by pushed call [{methodUrn}:{signalId}]");
                }
            },
            $"Error when call [{nameof(ProcessSignalLocally)}(signalId:{signalId}, methodUrn:{methodUrn})] in service [{_settings.CurrentServiceId}]");
    }

    [DisplayName("{0}")]
    public async Task EnqueueEffectionPerWorkflow(PotentialSignalEffection signalEffection)
    {
        var signalId = signalEffection.SignalId;
        //$"ServiceProcessSignal_{signalId}_{_settings.CurrentServiceId}",
        //todo:lock if there are many service instances
        await _backgroundJobExecutor.ExecuteWithoutLock(
            () =>
            {
                foreach (var workflowId in signalEffection.AffectedWorkflowsIds)
                {
                    _backgroundJobClient.Enqueue(
                        () => _signalsProcessor.ProcessSignalMatchesAsync(
                            workflowId, signalId, signalEffection.MethodGroupId, signalEffection.SignalDate));
                }
                return Task.CompletedTask;
            },
            $"Error when call [ServiceProcessSignal(signalId:{signalId}, methodUrn:{signalEffection.MethodUrn})] in service [{_settings.CurrentServiceId}]");
    }

    [DisplayName("[{0}]")]
    public async Task SendSignalToAnotherWorkflowService(PotentialSignalEffection callImpaction)
    {
        try
        {
            var actionUrl = $"{callImpaction.AffectedServiceUrl}{Constants.WorkflowsControllerUrl}/{Constants.ServiceProcessSignalAction}";
            await DirectHttpPost(actionUrl, callImpaction);// will go to WorkflowsController.ServiceProcessSignal action
        }
        catch (Exception)
        {
            _backgroundJobClient.Schedule(() => SendSignalToAnotherWorkflowService(callImpaction), TimeSpan.FromSeconds(3));
        }
    }

    private async Task DirectHttpPost(string actionUrl, PotentialSignalEffection callImapction)
    {
        var client = _httpClientFactory.CreateClient();
        var json = JsonSerializer.Serialize(callImapction);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsJsonAsync(actionUrl, content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();
        if (!(result == "1" || result == "-1"))
            throw new Exception("Expected result must be 1 or -1");
    }
}