
using System.Linq;

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

        /// <summary>
        /// Finds a child wait by name. Returns null if not found.
        /// Useful in MigrateActiveWait to inspect partial completion status.
        /// </summary>
        public WaitInfrastructureDto? Child(string waitName)
            => ChildWaits?.FirstOrDefault(w => w.WaitName == waitName);
    }
}
