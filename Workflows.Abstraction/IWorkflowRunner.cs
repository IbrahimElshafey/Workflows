
using System;
using System.Linq;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction
{
    public interface IObjectSerializer
    {

    }
    public interface IExpressionSerializer
    {

    }
    public interface IWorkflowRunner
    {
        WorkflowRunResult RunWorkflow(WorkflowRunContext runContext);
    }
}