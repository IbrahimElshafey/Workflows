using Microsoft.Extensions.Logging;
using Workflows.Publisher.Abstraction;
using Workflows.Publisher.InOuts;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Workflows.Publisher.Implementation;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Implementation
{
    public class FailedRequestHandler : Abstraction.IFailedRequestHandler
    {
        private readonly Abstraction.ISenderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<Implementation.FailedRequestHandler> _logger;
        private readonly Abstraction.IFailedRequestStore _failedRequestRepo;

        public FailedRequestHandler(
           Abstraction.ISenderSettings settings,
           IHttpClientFactory httpClientFactory,
           ILogger<Implementation.FailedRequestHandler> logger,
           Abstraction.IFailedRequestStore failedRequestRepo)
        {
            _settings = settings;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _failedRequestRepo = failedRequestRepo;
        }
        public async Task EnqueueFailedRequest(InOuts.FailedRequest failedRequest)
        {
            try
            {
                await _failedRequestRepo.Add(failedRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Can't save failed request.");
            }
        }

        public async Task HandleFailedRequests()
        {
            while (true)
            {
                await Task.Delay(_settings.CheckFailedRequestEvery);
                if (await _failedRequestRepo.HasRequests())
                    await CallFailedRequests();
            }
        }

        // ReSharper disable once WorkflowRecursiveOnAllPaths
        private async Task CallFailedRequests()
        {
            try
            {
                _logger.LogInformation("Start handling failed requests.");
                var requestsTasks = new List<Task>();
                foreach (var request in _failedRequestRepo.GetRequests())
                {
                    requestsTasks.Add(CallRequest(request));
                }
                await Task.WhenAll(requestsTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error when handling failed requests.");
            }
        }

        private async Task CallRequest(InOuts.FailedRequest request)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response =
                    await client.PostAsync(request.ActionUrl, new ByteArrayContent(request.Body));
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadAsStringAsync();
                if (result == "1" || result == "-1")
                    await _failedRequestRepo.Remove(request);
                else
                    throw new Exception("Expected result must be 1 or -1");
            }
            catch (Exception ex)
            {
                if (request != null)
                {
                    request.AttemptsCount++;
                    _logger.LogError(ex,
                        $"A request [{request.Key}] failed again for [{request.AttemptsCount}] times");
                    request.LastAttemptDate = DateTime.UtcNow;
                    await _failedRequestRepo.Update(request);
                }
            }
        }
    }
}
