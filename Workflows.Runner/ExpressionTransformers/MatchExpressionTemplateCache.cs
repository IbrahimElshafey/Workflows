using System.Collections.Concurrent;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MatchExpressionTemplateCache
    {
        private readonly ConcurrentDictionary<string, ExpressionTemplateCacheRecord> _cache = new();

        public bool TryGetValue(string key, out ExpressionTemplateCacheRecord compiledParts)
        {
            return _cache.TryGetValue(key, out compiledParts);
        }

        public bool TryAdd(string key, ExpressionTemplateCacheRecord compiledParts)
        {
            return _cache.TryAdd(key, compiledParts);
        }
    }
}
