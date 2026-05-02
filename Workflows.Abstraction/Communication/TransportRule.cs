using System;
using System.Linq;

namespace Workflows.Abstraction.Communication
{
    internal class TransportRule
    {
        public Type MessageType { get; }
        public Func<object, bool> Condition { get; }
        public Type TransportType { get; }
        public Type SubscriberType { get; }
        public string Address { get; } // <--- The Queue Name or URL

        public TransportRule(Type messageType, Func<object, bool> condition, Type transportType, Type subscriberType, string address)
        {
            MessageType = messageType;
            Condition = condition;
            TransportType = transportType;
            SubscriberType = subscriberType;
            Address = address;
        }
    }
}