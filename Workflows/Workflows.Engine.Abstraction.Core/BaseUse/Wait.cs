using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.BaseUse
{
    public class Wait
    {
        internal Wait(WaitEntity wait)
        {
            WaitEntity = wait;
        }

        internal WaitEntity WaitEntity { get; set; }
    }
}