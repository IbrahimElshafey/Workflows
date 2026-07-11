using System;

namespace Workflows.Runner.Cache
{
    internal class CommandTemplateCacheRecord
    {
        public Func<object, object, object, bool>? CompiledMatchDelegate { get; set; }
        public Func<object, object, string[]>? CompiledInstanceExactMatchExpression { get; set; }
        public string? ResultAction { get; set; }
    }
}
