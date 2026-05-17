using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Abstraction.Orchestration
{
    public interface IExternalScheduler
    {
        Task ProcessTimerWakeupAsync();
        Task CommitRunnerResultAsync();
    }
}
