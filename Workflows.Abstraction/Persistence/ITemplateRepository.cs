namespace Workflows.Abstraction.Persistence
{
    /// <summary>
    /// Persistence repository to save and retrieve compiled match expression templates.
    /// </summary>
    public interface ITemplateRepository
    {
        TemplateCacheRecordDto? GetTemplate(string templateHashKey);
        void SaveTemplate(TemplateCacheRecordDto dto);
    }

    public class TemplateCacheRecordDto
    {
        public string TemplateHashKey { get; set; } = string.Empty;
        public string SignalExactMatchPathsJson { get; set; } = "[]";
        public bool IsExactMatchFullMatch { get; set; }
        public bool IsGenericMatchFullMatch { get; set; }
        public string? GenericMatchExpressionJson { get; set; }
        public string? InstanceExactMatchExpressionJson { get; set; }
        public string? NormalizedMatchExpressionJson { get; set; }
    }
}
