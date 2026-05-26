using System;

namespace Workflows.Runner.Cache
{
    internal class SignalTemplateCacheRecord
    {
        public Func<object, object, object, bool> CompiledMatchDelegate { get; set; }
        public Func<object, object, string[]>? CompiledInstanceExactMatchExpression { get; set; }

        /// <summary>
        /// Serialized AfterMatchAction callback, cached from the template so the DB is not queried on every match.
        /// </summary>
        public string? AfterMatchAction { get; set; }
    }
}

