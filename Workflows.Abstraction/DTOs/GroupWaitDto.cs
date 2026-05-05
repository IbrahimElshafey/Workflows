using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    /// <summary>
    /// DTO for GroupWait that stores composite wait configuration.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with persistence infrastructure.
    /// </summary>
    public class GroupWaitDto : WaitInfrastructureDto
    {
        internal GroupWaitDto()
        {
            WaitType = WaitType.GroupWaitAll;
        }

        /// <summary>
        /// Name of the match function for custom group matching.
        /// </summary>
        public string MatchFuncName { get; internal set; }
    }
}
