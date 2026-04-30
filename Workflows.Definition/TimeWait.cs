using System;
using Workflows.Abstraction.DTOs;
namespace Workflows.Handler.BaseUse
{
    /// <summary>
    /// Represents a passive wait for a specified time duration or until a specific time.
    /// Time-based waits do not initiate side effects, so they can be safely combined
    /// with other passive waits in group scenarios.
    /// </summary>
    public class TimeWait : Wait, IPassiveWait
    {
        internal TimeWaitDto Data { get; }
        internal TimeWait(TimeWaitDto waitData) : base(waitData)
        {
            Data = waitData;
        }
        public TimeWait WhenCancel(Action cancelAction)
        {
            CancelAction = cancelAction;
            return this;
        }
    }
}