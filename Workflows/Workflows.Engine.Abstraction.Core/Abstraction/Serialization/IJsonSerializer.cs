namespace Workflows.Handler.Abstraction.Serialization
{
    /// <summary>
    /// Abstraction for JSON serialization to avoid direct dependency on JSON libraries
    /// </summary>
    public interface IJsonSerializer
    {
        /// <summary>
        /// Serializes an object to JSON string
        /// </summary>
        string Serialize(object value);

        /// <summary>
        /// Deserializes a JSON string to an object of the specified type
        /// </summary>
        T Deserialize<T>(string json);

        /// <summary>
        /// Deserializes a JSON string to an object of the specified type
        /// </summary>
        object Deserialize(string json, System.Type type);
    }
}
