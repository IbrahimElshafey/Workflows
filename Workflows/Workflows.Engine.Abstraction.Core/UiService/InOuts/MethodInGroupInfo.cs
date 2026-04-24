using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.UiService.InOuts
{
    public class MethodInGroupInfo
    {
        public MethodInGroupInfo(string ServiceName, MethodIdentifier Method, string GroupUrn)
        {
            this.ServiceName = ServiceName;
            this.Method = Method;
            this.GroupUrn = GroupUrn;
        }

        public string ServiceName { get; }
        public MethodIdentifier Method { get; }
        public string GroupUrn { get; }
    }
}