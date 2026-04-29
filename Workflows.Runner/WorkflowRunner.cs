using Microsoft.Extensions.Logging;
using Workflows.Abstraction.Common;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Runner.ExpressionTransformers;

namespace Workflows.Runner
{
    internal class WorkflowRunner : IWorkflowRunner
    {
        public WorkflowRunner(
            MatchExpressionTransformer matchExpressionTransformer,
            Abstraction.Runner.IExpressionSerializer expressionSerializer,
            IObjectSerializer objectSerializer,
            RunWorkflowSettings settings,
            IWorkflowRunResultSender runResultSender,
            ILogger<WorkflowRunner> logger)
        {

        }
        public WorkflowRunId RunWorkflow(WorkflowRunContext runContext)
        {
            //see WaitsProcessor.ResumeExecution()
            return RunWorkflow(runContext);
        }
    }
}
