using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Buffers;
using System.Text;

namespace Workflows.MvcUi
{
    [ApiController]
    [Route(Constants.WorkflowsControllerUrl)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class WorkflowsController : ControllerBase
    {
        public readonly ISignalDispatcher _callPusher;
        public readonly IServiceQueue _serviceQueue;
        private readonly IBackgroundProcess _backgroundProcess;
        private readonly ILogger<WorkflowsController> _logger;

        public WorkflowsController(
            ILogger<WorkflowsController> logger,
            ISignalDispatcher callPusher,
            IServiceQueue serviceQueue,
            IBackgroundProcess backgroundProcess)
        {
            _logger = logger;
            _callPusher = callPusher;
            _serviceQueue = serviceQueue;
            _backgroundProcess = backgroundProcess;
        }



        //todo:error in get reponse
        [HttpPost(Constants.ServiceProcessSignalAction)]
        public async Task<int> ServiceProcessSignalAsync(PotentialSignalEffection callEffection)
        {
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string requestBody = await reader.ReadToEndAsync();
                // Now you have the request body as a string
                // You can log it, parse it, or process it as needed

                // Example: log the request body
                Console.WriteLine(requestBody);

                // Process the requestBody here as needed
            }
            //todo:validate object
            await _serviceQueue.EnqueueEffectionPerWorkflow(callEffection);
            return 1;
        }

        [HttpPost(Constants.ExternalCallAction)]
        public async Task<int> ExternalCall()
        {
            var body = await Request.BodyReader.ReadAsync();
            var bytes = body.Buffer.ToArray();
            var serializer = new BinarySerializer();
            var externalCall = serializer.ConvertToObject<ExternalCallArgs>(bytes);
            return await ExternalCallJson(externalCall);
        }

        [HttpPost(Constants.ExternalCallAction + "Json")]
        public Task<int> ExternalCallJson(ExternalCallArgs externalCall)
        {
            return ReceiveExternalCall(externalCall);
        }

        public async Task<int> ReceiveExternalCall(ExternalCallArgs externalCall)
        {
            try
            {
                if (externalCall == null)
                    throw new ArgumentNullException(nameof(externalCall));

                var signal = new SignalEntity
                {
                    MethodData = externalCall.MethodData,
                    Data = new()
                    {
                        Input = externalCall.Input,
                        Output = externalCall.Output
                    },
                    Created = externalCall.Created,
                };
                signal.MethodData.CanPublishFromExternal = true;
                await _callPusher.EnqueueExternalSignalWork(signal, externalCall.ServiceName);
                return 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error when handle external method call.");
                return -1;
            }
        }
    }
}