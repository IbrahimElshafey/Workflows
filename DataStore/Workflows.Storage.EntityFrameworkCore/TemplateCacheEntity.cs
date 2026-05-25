using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class TemplateCacheEntity : IEntity
    {
        public string TemplateHashKey { get; set; } = string.Empty;
        public string SignalExactMatchPathsJson { get; set; } = "[]";
        public bool IsExactMatchFullMatch { get; set; }
        public bool IsGenericMatchFullMatch { get; set; }
        public string? GenericMatchExpressionJson { get; set; }
        public string? InstanceExactMatchExpressionJson { get; set; }
        public string? NormalizedMatchExpressionJson { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow;
    }
}
