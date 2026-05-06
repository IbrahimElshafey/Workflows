
namespace Workflows.Abstraction.DTOs.Waits
{
    /// <summary>
    /// DTO for GroupWait that stores composite wait configuration.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with persistence infrastructure.
    /// </summary>
    public class GroupWaitDto : WaitInfrastructureDto
    {
        /// <summary>
        /// Name of the match function for custom group matching.
        /// </summary>
        public string MatchFuncName { get; internal set; }
        public string MatchFuncClosureKey { get; internal set; }
    }
}
