namespace Workflows.Handler.BaseUse
{
    /// <summary>
    /// Marker interface for active waits that initiate side effects (e.g., commands, API calls).
    /// Active waits must not be combined with MatchAny() to prevent race conditions where
    /// multiple commands could execute. They should only use MatchAll() via ExecuteParallel().
    /// Examples: Command
    /// </summary>
    public interface IActiveWait
    {
    }
}
