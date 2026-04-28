
using System;
using Workflows.Abstraction.DTOs;
namespace Workflows.Handler.BaseUse
{
    public class TimeWait : Wait
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