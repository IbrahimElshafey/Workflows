using System;
namespace Workflows.Shared.Communication
{
    internal class TransportRule
    {
        public Type MessageType { get; }
        public Func<object, bool> Condition { get; }
        public Type TransportType { get; }
        public Type SubscriberType { get; }
        public string Address { get; } // <--- The Queue Name, URL,or Pipe Name

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