using System.Linq.Expressions;

namespace Workflows.Handler.BaseUse
{
    public interface ISignalWait
    {
        LambdaExpression MatchExpression { get; set; }
        WorkflowContainer CurrentWorkflow { get; set; }
    }
}