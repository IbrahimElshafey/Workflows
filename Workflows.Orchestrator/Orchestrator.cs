using System;
using System.Collections.Generic;
using System.Text;
using Workflows.Abstraction.Communication;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Runner;

namespace Workflows.Orchestrator
{
    public class Orchestrator : IOrchestrator
    {
        public Task ProcessCommandResultAsync(Guid commandWaitId, object result)
        {
            throw new NotImplementedException();
        }

        public Task ProcessSignalAsync(string signalPath, object payload)
        {
            throw new NotImplementedException();
        }

        public Task<Guid> StartWorkflowAsync(string workflowName, string version, object input)
        {
            throw new NotImplementedException();
        }
    }
}
