using Workflows.Handler.Attributes;
using Workflows.Handler.InOuts;
using System.ComponentModel;
namespace Workflows.Handler.Helpers
{
    public class LocalRegisteredMethods
    {
        [DisplayName("{0}")]
        public bool TimeWait(TimeWaitInput timeWaitInput)
        {
            return true;
        }
    }
}
