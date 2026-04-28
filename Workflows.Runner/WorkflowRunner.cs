using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Workflows.Abstraction;
using Workflows.Abstraction.DTOs;
using Workflows.Handler.BaseUse;
using Workflows.Runner.ExpressionTransformers;

namespace Workflows.Runner
{
    internal class WorkflowRunner : IWorkflowRunner
    {
        public WorkflowRunner(
            MatchExpressionTransformer matchExpressionTransformer,
            IExpressionSerializer expressionSerializer,
            IObjectSerializer objectSerializer,
            RunWorkflowSettings settings,
            ILogger<WorkflowRunner> logger)
        {
            
        }
        public WorkflowRunResult RunWorkflow(WorkflowRunContext runContext)
        {
            //see WaitsProcessor.ResumeExecution()
            return RunWorkflow(runContext);
        }
    }
}
