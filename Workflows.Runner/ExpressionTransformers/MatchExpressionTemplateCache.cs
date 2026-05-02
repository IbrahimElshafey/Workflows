using System.Collections.Concurrent;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MatchExpressionTemplateCache
    {
        private readonly ConcurrentDictionary<string, ExpressionTemplateCachRecord> _cache = new();

        public bool TryGetValue(string key, out ExpressionTemplateCachRecord compiledParts)
        {
            return _cache.TryGetValue(key, out compiledParts);
        }

        public bool TryAdd(string key, ExpressionTemplateCachRecord compiledParts)
        {
            return _cache.TryAdd(key, compiledParts);
        }
    }
}
