using System.Collections.Generic;
using Workflows.Handler.InOuts;
namespace Workflows.Handler.UiService.InOuts
{
    public class SignalDetails
    {
        public string InputOutput { get; }
        public MethodData MethodData { get; }
        public List<MethodWaitDetails> Waits { get; }

        public SignalDetails(string inputOutput, MethodData methodData, List<MethodWaitDetails> waits)
        {
            InputOutput = inputOutput;
            MethodData = methodData;
            Waits = waits;
        }
    }
}
