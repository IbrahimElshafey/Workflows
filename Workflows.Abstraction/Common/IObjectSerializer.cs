
using System;

namespace Workflows.Abstraction.Common
{
    /// <summary>
    /// Defines a pluggable mechanism for serializing and deserializing workflow data,
    /// supporting both clean data transfer and complex internal state hydration.
    /// </summary>
    public interface IObjectSerializer
    {
        /// <summary>
        /// Serializes an object based on the requested scope.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="scope">The scope determining how strictly to serialize fields and types.</param>
        string Serialize(object obj, Enums.SerializationScope scope = Enums.SerializationScope.Standard);

        /// <summary>
        /// Deserializes a string back into an object of a specific type.
        /// </summary>
        T Deserialize<T>(string serializedObj, Enums.SerializationScope scope = Enums.SerializationScope.Standard);

        /// <summary>
        /// Deserializes a string into a specific type determined at runtime.
        /// </summary>
        object Deserialize(string serializedObj, Type type, Enums.SerializationScope scope = Enums.SerializationScope.Standard);
    }
}
namespace Workflows.Abstraction.Enums
{
}