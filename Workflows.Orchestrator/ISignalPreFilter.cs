using System;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Orchestrator
{
    public interface ISignalPreFilter
    {
        bool IsMatch(SignalWaitDto signalWait, SignalDto signalDto, WorkflowStateDto state);
    }
}
