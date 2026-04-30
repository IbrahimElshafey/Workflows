using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    public class WaitsGroupDto : WaitBaseDto
    {

        internal WaitsGroupDto()
        {
            WaitType = WaitType.GroupWaitAll;
        }

        public string MatchFuncName { get; internal set; }
    }
}