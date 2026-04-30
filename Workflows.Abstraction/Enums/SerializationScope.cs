namespace Workflows.Abstraction.Enums
{
    /// <summary>
    /// Specifies the target scope for serialization, allowing the engine to 
    /// handle standard data and complex compiler-generated state differently.
    /// </summary>
    public enum SerializationScope
    {
        /// <summary>
        /// Used for Signals, public Workflow class state, and developer-defined DTOs.
        /// Produces clean, portable, and version-tolerant output.
        /// </summary>
        Standard,

        /// <summary>
        /// Used for internal state machine closures (<>c__DisplayClass) and locals.
        /// Captures private fields, backing fields, and strict type metadata required for resumption.
        /// </summary>
        CompilerGeneratedClass
    }
}