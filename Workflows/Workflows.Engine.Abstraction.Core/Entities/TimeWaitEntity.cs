using System;
using Workflows.Handler.BaseUse;
using Workflows.Handler.Helpers;
namespace Workflows.Handler.InOuts.Entities
{
    public class TimeWaitEntity : WaitEntity
    {
        internal readonly MethodWaitEntity<TimeWaitInput, bool> _timeMethodWait;

        internal TimeWaitEntity(WorkflowContainer currentWorkflow)
        {
            var timeWaitMethod = typeof(LocalRegisteredMethods)
                            .GetMethod(nameof(LocalRegisteredMethods.TimeWait));

            _timeMethodWait =
                new MethodWaitEntity<TimeWaitInput, bool>(timeWaitMethod) { CurrentWorkflow = currentWorkflow };
        }

        public TimeSpan TimeToWait { get; internal set; }
        public bool IgnoreJobCreation { get; internal set; }
        internal string UniqueMatchId { get; set; }
        internal MethodWaitEntity TimeWaitMethod
        {
            get
            {
                _timeMethodWait.Name = Name ?? $"#Time Wait for `{TimeToWait.TotalHours}` hours in `{CallerName}`";
                _timeMethodWait.MethodWaitType = MethodWaitType.TimeWaitMethod;
                _timeMethodWait.CurrentWorkflow = CurrentWorkflow;
                _timeMethodWait.IsFirst = IsFirst;
                _timeMethodWait.WasFirst = WasFirst;
                _timeMethodWait.IsRoot = IsRoot;
                _timeMethodWait.ParentWait = ParentWait;
                _timeMethodWait.WorkflowInstance = WorkflowInstance;
                _timeMethodWait.RequestedByWorkflowId = RequestedByWorkflowId;
                _timeMethodWait.StateAfterWait = StateAfterWait;
                _timeMethodWait.Locals = Locals;
                _timeMethodWait.CallerName = CallerName;
                _timeMethodWait.Created = Created;
                _timeMethodWait.InCodeLine = InCodeLine;
                _timeMethodWait.ExtraData =
                    new TimeWaitExtraData
                    {
                        TimeToWait = TimeToWait,
                        UniqueMatchId = UniqueMatchId,
                    };
                _timeMethodWait.MatchIf((timeWaitInput, result) => timeWaitInput.TimeMatchId == string.Empty);
                return _timeMethodWait;
            }
        }

        internal TimeWait ToTimeWait() => new TimeWait(this);
    }
}