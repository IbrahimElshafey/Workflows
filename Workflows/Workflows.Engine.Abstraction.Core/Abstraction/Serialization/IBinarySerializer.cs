namespace Workflows.Handler.Abstraction.Serialization
{
    /// <summary>
    /// Abstraction for binary serialization to avoid direct dependency on serialization libraries
    /// </summary>
    public interface IBinarySerializer
    {
        /// <summary>
        /// Serializes an object to byte array
        /// </summary>
        byte[] Serialize(object value);

        /// <summary>
        /// Deserializes a byte array to an object of the specified type
        /// </summary>
        object Deserialize(byte[] data, System.Type type);

        /// <summary>
        /// Deserializes a byte array to an object of the specified type
        /// </summary>
        T Deserialize<T>(byte[] data);
    }
}
