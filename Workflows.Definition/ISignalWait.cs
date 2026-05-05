using System.Linq.Expressions;

namespace Workflows.Definition
{
    public interface ISignalWait
    {
        LambdaExpression MatchExpression { get; set; }
        WorkflowContainer WorkflowContainer { get; set; }
        string SignalIdentifier { get; }
        object AfterMatchAction { get; }
    }
}