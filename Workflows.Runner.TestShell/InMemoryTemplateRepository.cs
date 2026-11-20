using System;
using System.Collections.Concurrent;
using Workflows.Abstraction.Persistence;

namespace Workflows.TestShell
{
    /// <summary>
    /// Process-lifetime <see cref="ITemplateRepository"/> stand-in for the test shell.
    /// The orchestrator's EF Core implementation persists templates in the database, which is what
    /// keeps workflow callbacks resolvable after a worker restart. Holding the same records in memory
    /// lets the shell model that restart when it rebuilds its service provider. One instance is owned
    /// by the shell, so the records outlive every provider it creates.
    /// </summary>
    public sealed class InMemoryTemplateRepository : ITemplateRepository
    {
        private readonly ConcurrentDictionary<string, TemplateCacheRecordDto> _templates = new(StringComparer.Ordinal);

        public TemplateCacheRecordDto? GetTemplate(string templateHashKey) =>
            !string.IsNullOrEmpty(templateHashKey) && _templates.TryGetValue(templateHashKey, out var dto)
                ? Clone(dto)
                : null;

        public void SaveTemplate(TemplateCacheRecordDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            _templates[dto.TemplateHashKey] = Clone(dto);
        }

        private static TemplateCacheRecordDto Clone(TemplateCacheRecordDto dto) => new()
        {
            TemplateHashKey = dto.TemplateHashKey,
            SignalExactMatchPathsJson = dto.SignalExactMatchPathsJson,
            IsExactMatchFullMatch = dto.IsExactMatchFullMatch,
            IsGenericMatchFullMatch = dto.IsGenericMatchFullMatch,
            GenericMatchExpressionJson = dto.GenericMatchExpressionJson,
            InstanceExactMatchExpressionJson = dto.InstanceExactMatchExpressionJson,
            NormalizedMatchExpressionJson = dto.NormalizedMatchExpressionJson,
            AfterMatchAction = dto.AfterMatchAction,
            CancelAction = dto.CancelAction
        };
    }
}