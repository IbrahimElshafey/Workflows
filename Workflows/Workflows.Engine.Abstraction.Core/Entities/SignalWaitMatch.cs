using Workflows.Handler.InOuts.Entities.EntityBehaviour;

using System;
namespace Workflows.Handler.InOuts.Entities
{
    public class SignalWaitMatch : IEntity<long>, IEntityWithUpdate
    {
        public long Id { get; internal set; }
        public long SignalId { get; internal set; }
        public long WaitId { get; internal set; }
        public int? ServiceId { get; internal set; }
        public int WorkflowId { get; internal set; }
        public int StateId { get; internal set; }
        public int TemplateId { get; internal set; }
        public MatchStatus MatchStatus { get; internal set; } = MatchStatus.PotentialMatch;
        public ExecutionStatus AfterMatchActionStatus { get; internal set; } = ExecutionStatus.NotStartedYet;
        public ExecutionStatus ExecutionStatus { get; internal set; } = ExecutionStatus.NotStartedYet;
        public DateTime Created { get; internal set; }

        public DateTime Modified { get; internal set; }
        public string ConcurrencyToken { get; internal set; }

        public override bool Equals(object obj)
        {
            if (obj is SignalWaitMatch waitProcessingRecord)
            {
                return
                    waitProcessingRecord.WaitId == WaitId &&
                    waitProcessingRecord.WorkflowId == WorkflowId &&
                    waitProcessingRecord.StateId == StateId;
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return $"{WaitId}-{WorkflowId}-{StateId}".GetHashCode();
        }
    }
}
