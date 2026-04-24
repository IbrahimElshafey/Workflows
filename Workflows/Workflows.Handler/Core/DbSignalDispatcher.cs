using Microsoft.Extensions.Logging;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Reflection;

namespace Workflows.Handler.Core
{
    internal class DbSignalDispatcher : ISignalDispatcher
    {
        private readonly IUnitOfWork _context;
        private readonly IBackgroundProcess _backgroundProcess;
        private readonly IServiceQueue _serviceQueue;
        private readonly ILogger<DbSignalDispatcher> _logger;
        private readonly ISignalsStore _signalsRepo;
        private readonly IMethodIdentifiersStore _methodIdsRepo;
        private readonly ILogsRepo _serviceRepo;

        public DbSignalDispatcher(
            IUnitOfWork context,
            IBackgroundProcess backgroundProcess,
            IServiceQueue serviceQueue,
            ILogger<DbSignalDispatcher> logger,
            ISignalsStore signalsRepo,
            IMethodIdentifiersStore methodIdsRepo,
            ILogsRepo serviceRepo)
        {
            _context = context;
            _backgroundProcess = backgroundProcess;
            _serviceQueue = serviceQueue;
            _logger = logger;
            _signalsRepo = signalsRepo;
            _methodIdsRepo = methodIdsRepo;
            _serviceRepo = serviceRepo;
        }

        public async Task<long> EnqueueLocalSignalWork(SignalEntity signal)
        {
            try
            {
                await _signalsRepo.SaveSignal(signal);
                await _context.CommitAsync();
                _backgroundProcess.Enqueue(() =>
                    _serviceQueue.IdentifyAffectedWorkflows(signal.Id, DateTime.UtcNow, signal.MethodData.MethodUrn));
                return signal.Id;
            }
            catch (Exception ex)
            {
                var error = $"Can't handle pushed call [{signal}]";
                await _serviceRepo.AddErrorLog(ex, error, StatusCodes.Signal);
                throw new Exception(error, ex);
            }
        }
        public async Task<long> EnqueueExternalSignalWork(SignalEntity signal, string serviceName)
        {
            try
            {
                var currentServiceName = Assembly.GetEntryAssembly().GetName().Name;
                if (serviceName != currentServiceName)
                {
                    await _serviceRepo.AddErrorLog(
                        null,
                        $"Pushed call target service [{serviceName}] but the current service is [{currentServiceName}]" +
                        $"\nPushed call was [{signal}]",
                        StatusCodes.Signal);
                    return -1;
                }

                var methodUrn = signal.MethodData.MethodUrn;
                if (await _methodIdsRepo.CanPublishFromExternal(methodUrn))
                {
                    await _signalsRepo.SaveSignal(signal);
                    await _context.CommitAsync();
                    //Route call to current service only
                    _backgroundProcess.Enqueue(() =>
                        _serviceQueue.ProcessSignalLocally(signal.Id, signal.MethodData.MethodUrn, signal.Created));

                    return signal.Id;
                }


                await _serviceRepo.AddLog(
                    $"There is no method with URN [{methodUrn}] that can be called from external in service [{serviceName}].\nPushed call was [{signal}]",
                    LogType.Warning,
                    StatusCodes.Signal);
                return -1;
            }
            catch (Exception ex)
            {
                var error = $"Can't handle external pushed call [{signal}]";
                await _serviceRepo.AddErrorLog(ex, error, StatusCodes.Signal);
                throw new Exception(error, ex);
            }
        }
    }
}