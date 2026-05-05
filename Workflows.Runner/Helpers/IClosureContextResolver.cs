using System.Linq.Expressions;
using Workflows.Definition;

namespace Workflows.Runner.Helpers
{
    internal interface IClosureContextResolver
    {
        string CacheClosureIfAny(object actionTarget, Wait wait);
        object TryGetClosureFromExpression(Expression expr);
    }
}
