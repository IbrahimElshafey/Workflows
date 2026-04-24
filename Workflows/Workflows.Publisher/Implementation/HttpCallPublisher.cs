using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Logging;
using Workflows.Sender.Abstraction;
using Workflows.Sender.Helpers;
using Workflows.Sender.InOuts;
using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Workflows.Sender.Implementation
{
    public class HttpCallSender : ISignalSender
    {
        private readonly ISenderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HttpCallSender> _logger;
        private readonly IFailedRequestHandler _failedRequestHandler;

        public HttpCallSender(
            ISenderSettings settings,
            IHttpClientFactory httpClientFactory,
            ILogger<HttpCallSender> logger,
            IFailedRequestHandler failedRequestHandler)
        {
            _settings = settings;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _failedRequestHandler = failedRequestHandler;
        }

        public async Task Send<TInput, TOutput>(Func<TInput, Task<TOutput>> methodToPush,
            TInput input,
            TOutput output,
            string methodUrn,
            params string[] toServices)
        {
            var minfo = methodToPush.Method;
            await Send(new MethodCall
            {
                MethodData = new MethodData
                {
                    MethodUrn = methodUrn,
                    AssemblyName = "[From Client] " + Assembly.GetEntryAssembly()?.GetName().Name,
                    ClassName = minfo.DeclaringType.Name,
                    MethodName = minfo.Name,
                },
                Input = input,
                Output = output,
                ToServices = toServices
            });
        }

        public async Task Send(MethodCall methodCall)
        {
            //D:\GAFT\Workflows\Workflows.AspNetService\WorkflowsController.cs
            foreach (var service in methodCall.ToServices)
            {
                var serviceUrl = _settings.ServicesRegistry[service];
                string actionUrl =
                    $"{serviceUrl}{Constants.WorkflowsControllerUrl}/{Constants.ExternalCallAction}";
                byte[] body = null;
                try
                {
                    methodCall.ServiceName = service;
                    body = MessagePackSerializer.Serialize(methodCall, ContractlessStandardResolver.Options);
                    var client = _httpClientFactory.CreateClient();
                    var response = await client.PostAsync(actionUrl, new ByteArrayContent(body));
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadAsStringAsync();
                    if (!(result == "1" || result == "-1"))
                        throw new Exception("Expected result must be 1 or -1");
                }
                catch (Exception ex)
                {
                    var failedRequest = new FailedRequest(Guid.NewGuid(), DateTime.UtcNow, actionUrl, body);
                    _logger.LogError(ex, $"Error occurred when publish method call {methodCall}");
                    _ = _failedRequestHandler.EnqueueFailedRequest(failedRequest);
                }
            }
        }
    }
}
