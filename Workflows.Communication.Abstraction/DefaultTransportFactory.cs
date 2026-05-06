using System;
using System.Linq;




namespace Workflows.Communication.Abstraction
{
    public class DefaultTransportFactory : ITransportFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TransportRoutingBuilder _routingConfig;

        public DefaultTransportFactory(IServiceProvider serviceProvider, TransportRoutingBuilder routingConfig)
        {
            _serviceProvider = serviceProvider;
            _routingConfig = routingConfig;
        }

        public IMessageTransport GetTransport<T>(T message)
        {
            // 1. Find the first rule that matches the Type AND the compiled value condition
            var matchedRule = _routingConfig.Rules.FirstOrDefault(r =>
                r.MessageType.IsAssignableFrom(typeof(T)) && r.Condition(message));

            Type transportType = matchedRule?.TransportType ?? _routingConfig.GetDefaultTransport();

            // 2. Resolve from DI
            var transport = (IMessageTransport)_serviceProvider.GetService(transportType);
            if (transport == null)
            {
                throw new InvalidOperationException($"No messageSubscriber found for type {transportType.FullName}");
            }
            return transport;
        }

        public IMessageSubscriber GetSubscriber<T>()
        {
            // 1. For subscribers, we only match by Type (since there is no instance yet)
            var matchedRule = _routingConfig.Rules.FirstOrDefault(r =>
                r.MessageType.IsAssignableFrom(typeof(T)));

            Type subscriberType = matchedRule?.SubscriberType ?? _routingConfig.GetDefaultSubscriber();

            // 2. Resolve from DI
            var messageSubscriber = (IMessageSubscriber)_serviceProvider.GetService(subscriberType);
            if (messageSubscriber == null)
            {
                throw new InvalidOperationException($"No messageSubscriber found for type {subscriberType.FullName}");
            }
            return messageSubscriber;
        }
    }
}