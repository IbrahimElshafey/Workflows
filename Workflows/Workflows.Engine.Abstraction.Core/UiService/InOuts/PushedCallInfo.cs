using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.UiService.InOuts
{
    public class SignalInfo
    {
        public SignalEntity Signal { get; }
        public int ExpectedMatchCount { get; }
        public int MatchedCount { get; }
        public int NotMatchedCount { get; }

        public SignalInfo(SignalEntity Signal, int ExpectedMatchCount, int MatchedCount, int NotMatchedCount)
        {
            this.Signal = Signal;
            this.ExpectedMatchCount = ExpectedMatchCount;
            this.MatchedCount = MatchedCount;
            this.NotMatchedCount = NotMatchedCount;
        }
    }
}
