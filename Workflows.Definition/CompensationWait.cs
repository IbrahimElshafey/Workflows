using Workflows.Primitives;

namespace Workflows.Definition
{
    public partial class CompensationWait : Wait
    {
        internal string Token { get; set; }
        internal CompensationWait(
            string token,
            WaitType waitType,
            int inCodeLine,
            string callerName,
            string callerFilePath) : base(waitType, null, inCodeLine, callerName, callerFilePath)
        {
            Token = token;
        }

        internal CompensationWait()
        {
        }
    }
}

