using System;
using System.Threading.Tasks;
using Workflows.Runner.Helpers;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// No-op delegate serializer for testing (delegates are kept in memory)
    /// </summary>
    internal class TestDelegateSerializer : IDelegateSerializer
    {
        public string Serialize(Delegate @delegate)
        {
            // In-memory tests keep delegates alive, no serialization needed
            return @delegate?.Method?.Name ?? string.Empty;
        }

        public Delegate Deserialize(string serialized, Type delegateType)
        {
            // In-memory tests don't deserialize delegates
            throw new NotImplementedException("Delegate deserialization not needed for in-memory tests");
        }

        public Func<T, ValueTask> Deserialize<T>(string serialized)
        {
            throw new NotImplementedException("Delegate deserialization not needed for in-memory tests");
        }
    }
}
