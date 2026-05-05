namespace Workflows.Definition
{
    public partial class CompensationWait : Wait
    {
        public string Token { get; }
        public CompensationWait(
            string token,
            WaitType waitType,
            int inCodeLine,
            string callerName,
            string callerFilePath) : base(waitType, null, inCodeLine, callerName, callerFilePath)
        {
            Token = token;
        }
    }
}

