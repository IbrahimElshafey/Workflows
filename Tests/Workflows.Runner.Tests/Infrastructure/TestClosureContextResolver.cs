using System.Collections.Concurrent;
using System.Linq.Expressions;
using Workflows.Definition;
using Workflows.Runner.Helpers;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// In-memory closure context resolver for testing
    /// </summary>
    internal class TestClosureContextResolver : IClosureContextResolver
    {
        private readonly ConcurrentDictionary<string, object> _closureCache = new();
        private int _keyCounter = 0;

        public string CacheClosureIfAny(object closure, Wait wait)
        {
            if (closure == null) return null;

            var key = $"closure_{System.Threading.Interlocked.Increment(ref _keyCounter)}";
            _closureCache[key] = closure;
            return key;
        }

        public object TryGetClosureFromExpression(Expression expr)
        {
            // In-memory tests don't extract closures from expressions
            return null;
        }
    }
}
