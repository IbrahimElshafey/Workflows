using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.Abstraction.DataAccess.InOuts
{
    public class PendingWaitData
    {
        public PendingWaitData(long WaitId, WaitTemplate Template, string MandatoryPart, bool IsFirst)
        {
            this.WaitId = WaitId;
            this.Template = Template;
            this.MandatoryPart = MandatoryPart;
            this.IsFirst = IsFirst;
        }

        public long WaitId { get; }
        public WaitTemplate Template { get; }
        public string MandatoryPart { get; }
        public bool IsFirst { get; }
    }
}
