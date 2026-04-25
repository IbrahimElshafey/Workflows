using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Logging;
using Workflows.Publisher.Abstraction;
using Workflows.Publisher.Helpers;
using Workflows.Publisher.InOuts;
using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Workflows.Publisher.Implementation;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Implementation
{
    public class HttpCallSender : Abstraction.ISignalSender
    {
        private readonly Abstraction.ISenderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<Implementation.HttpCallSender> _logger;
        private readonly Abstraction.IFailedRequestHandler _failedRequestHandler;

        public HttpCallSender(
            Abstraction.ISenderSettings settings,
            IHttpClientFactory httpClientFactory,
            ILogger<Implementation.HttpCallSender> logger,
            Abstraction.IFailedRequestHandler failedRequestHandler)
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
            await Send(new InOuts.MethodCall
            {
                MethodData = new InOuts.MethodData
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

        public async Task Send(InOuts.MethodCall methodCall)
        {
            //D:\GAFT\Workflows\Workflows.AspNetService\WorkflowsController.cs
            foreach (var service in methodCall.ToServices)
            {
                var serviceUrl = _settings.ServicesRegistry[service];
                string actionUrl =
                    $"{serviceUrl}{Helpers.Constants.WorkflowsControllerUrl}/{Helpers.Constants.ExternalCallAction}";
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
                    var failedRequest = new InOuts.FailedRequest(Guid.NewGuid(), DateTime.UtcNow, actionUrl, body);
                    _logger.LogError(ex, $"Error occurred when publish method call {methodCall}");
                    _ = _failedRequestHandler.EnqueueFailedRequest(failedRequest);
                }
            }
        }
    }
}
