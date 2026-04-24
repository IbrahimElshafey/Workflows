using EnsureThat;
using Workflows.Sender.Abstraction;
using System;
using System.Collections.Generic;

namespace Workflows.Sender.Implementation
{
    public class SenderSettings : ISenderSettings
    {

        public SenderSettings(Dictionary<string, string> servicesRegistry, TimeSpan checkFailedRequestEvery = default)
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
