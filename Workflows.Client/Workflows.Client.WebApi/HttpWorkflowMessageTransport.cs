using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Workflows.Abstraction.Helpers;
using Workflows.Communication.Abstraction;

namespace Workflows.Client.WebApi
{
    /// <summary>
    /// Implements <see cref="IMessageTransport"/> over HTTP.
    /// Serializes and sends messages via POST requests to the URL specified by <c>destination</c>.
    /// </summary>
    public class HttpWorkflowMessageTransport : IMessageTransport
    {
        private readonly HttpClient _httpClient;
        private readonly IObjectSerializer _serializer;

        public HttpWorkflowMessageTransport(HttpClient httpClient, IObjectSerializer serializer)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public async Task SendAsync<T>(string destination, T message)
        {
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentNullException(nameof(destination));
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var serialized = _serializer.Serialize(message);
            var jsonString = serialized as string ?? serialized?.ToString() ?? string.Empty;
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(destination, content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        public async Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message)
        {
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentNullException(nameof(destination));
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var serialized = _serializer.Serialize(message);
            var jsonString = serialized as string ?? serialized?.ToString() ?? string.Empty;
            var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(destination, content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return _serializer.Deserialize<TResponse>(responseJson);
        }
    }
}
