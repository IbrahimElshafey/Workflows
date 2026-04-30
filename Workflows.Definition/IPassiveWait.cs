namespace Workflows.Handler.BaseUse
{
    /// <summary>
    /// Marker interface for passive waits that react to external events without initiating side effects.
    /// Passive waits can be safely combined in groups with MatchAny() semantics.
    /// Examples: SignalWait, TimeWait, SubWorkflowWait
    /// </summary>
    public interface IPassiveWait
    {
    }
}
