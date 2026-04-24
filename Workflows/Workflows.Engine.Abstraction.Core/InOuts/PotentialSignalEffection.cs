using System;
using System.Collections.Generic;

namespace Workflows.Handler.InOuts
{
    public class PotentialSignalEffection
    {

        public int AffectedServiceId { get; internal set; }
        public string AffectedServiceName { get; internal set; }
        public string AffectedServiceUrl { get; internal set; }
        public List<int> AffectedWorkflowsIds { get; internal set; }

        public long SignalId { get; internal set; }
        public string MethodUrn { get; internal set; }
        public int MethodGroupId { get; internal set; }
        public DateTime SignalDate { get; internal set; }


        public override string ToString()
        {
            return $"Put pushed call [{MethodUrn}:{SignalId}] in the processing queue for service [{AffectedServiceName}].";
        }
    }
}
