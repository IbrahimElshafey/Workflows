using EnsureThat;
using Workflows.Publisher.Abstraction;
using System;
using System.Collections.Generic;
using Workflows.Publisher.Implementation;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Implementation
{
    public class PublisherSettings : Abstraction.ISenderSettings
    {

        public PublisherSettings(Dictionary<string, string> servicesRegistry, TimeSpan checkFailedRequestEvery = default)
        {
            Ensure.That(servicesRegistry).IsNotNull();
            Ensure.That(servicesRegistry).HasItems();
            ServicesRegistry = servicesRegistry;
            if (checkFailedRequestEvery != default)
                CheckFailedRequestEvery = checkFailedRequestEvery;
        }

        //todo: convert this to method that is generic
        //also IFailedRequestHandler, IFailedRequestRepo
        public Type SignalSenderType => typeof(HttpCallSender);

        public Dictionary<string, string> ServicesRegistry { get; }

        public TimeSpan CheckFailedRequestEvery { get; } = TimeSpan.FromMinutes(30);
    }
}
