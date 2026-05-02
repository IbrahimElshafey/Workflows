
using System;
using System.Linq;
using System.Threading.Tasks;
namespace Workflows.Abstraction.Communication
{
    public class DefaultMessageDispatcher : IMessageDispatcher
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TransportRoutingBuilder _routingConfig;

        public DefaultMessageDispatcher(IServiceProvider sp, TransportRoutingBuilder config)
        {
            _serviceProvider = sp;
            _routingConfig = config;
        }

        // Fire and Forget
        public async Task DispatchAsync<T>(T message)
        {
            var rule = FindRule(message);
            var transport = (IMessageTransport)_serviceProvider.GetService(rule.TransportType);
            if (transport == null)
            {
                throw new InvalidOperationException($"Service not registered for type: {rule.TransportType.Name}");
            }
            await transport.SendAsync(rule.Address, message);
        }

        // Request & Response (e.g., for Registration Sync)
        public async Task<TResponse> DispatchAndReceiveAsync<TRequest, TResponse>(TRequest message)
        {
            // 1. Find the matching rule using the compiled expression
            var rule = FindRule(message);

            // 2. Resolve the concrete transport (e.g., HttpTransport, RabbitMqTransport)
            var transport = (IMessageTransport)_serviceProvider.GetService(rule.TransportType);
            if (transport == null)
            {
                throw new InvalidOperationException($"Service not registered for type: {rule.TransportType.Name}");
            }
            // 3. Pass the message and the rule's Address, and await the typed response
            return await transport.SendAndReceiveAsync<TRequest, TResponse>(rule.Address, message);
        }

        // Helper method to keep code DRY
        private TransportRule FindRule<T>(T message)
        {
            var rule = _routingConfig.Rules.FirstOrDefault(r =>
                r.MessageType.IsAssignableFrom(typeof(T)) && r.Condition(message));

            if (rule == null)
            {
                throw new InvalidOperationException(
                    $"No routing rule found for message type: {typeof(T).Name} " +
                    $"with the provided values. Ensure it is registered in the TransportRoutingBuilder.");
            }

            return rule;
        }
    }
}