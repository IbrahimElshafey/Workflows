using FastExpressionCompiler;
using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Workflows.Runner
{
    //todo: delete this class
    //and use MatchExpressionTemplateCache instead, which will cache the compiled expression template and the compiled match function separately, so that we can reuse the compiled match function for different waits with the same match expression template.
    internal class MatchExpressionCache
    {
        private readonly ConcurrentDictionary<string, Func<object, object, bool>> _cache = new();

        public Func<object, object, bool> GetOrCompile(string templateHash, Func<LambdaExpression> deserializeFunc)
        {
            return _cache.GetOrAdd(templateHash, _ => CompileExpression(deserializeFunc()));
        }

        private Func<object, object, bool> CompileExpression(LambdaExpression expression)
        {
            return (Func<object, object, bool>)expression.CompileFast();
        }
    }
}
