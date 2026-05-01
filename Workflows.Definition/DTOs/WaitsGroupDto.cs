namespace Workflows.Definition.DTOs
{
    /// <summary>
    /// DTO for GroupWait that stores composite wait configuration.
    /// Inherits from WaitInfrastructureDto to maintain compatibility with persistence infrastructure.
    /// </summary>
    public class WaitsGroupDto : DTOs.WaitInfrastructureDto
    {
        internal WaitsGroupDto()
        {
            WaitType = Enums.WaitType.GroupWaitAll;
        }

        /// <summary>
        /// Name of the match function for custom group matching.
        /// </summary>
        public string MatchFuncName { get; internal set; }
    }
}