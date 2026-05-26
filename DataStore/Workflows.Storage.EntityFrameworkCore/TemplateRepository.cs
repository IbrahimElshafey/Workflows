using System;
using Workflows.Abstraction.Persistence;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class TemplateRepository : ITemplateRepository
    {
        private readonly WorkflowsDbContext _dbContext;

        public TemplateRepository(WorkflowsDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public TemplateCacheRecordDto? GetTemplate(string templateHashKey)
        {
            var entity = _dbContext.Set<TemplateCacheEntity>().Find(templateHashKey);
            if (entity == null) return null;

            return new TemplateCacheRecordDto
            {
                TemplateHashKey = entity.TemplateHashKey,
                SignalExactMatchPathsJson = entity.SignalExactMatchPathsJson,
                IsExactMatchFullMatch = entity.IsExactMatchFullMatch,
                IsGenericMatchFullMatch = entity.IsGenericMatchFullMatch,
                GenericMatchExpressionJson = entity.GenericMatchExpressionJson,
                InstanceExactMatchExpressionJson = entity.InstanceExactMatchExpressionJson,
                NormalizedMatchExpressionJson = entity.NormalizedMatchExpressionJson,
                AfterMatchAction = entity.AfterMatchAction,
                CancelAction = entity.CancelAction
            };
        }

        public void SaveTemplate(TemplateCacheRecordDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var entity = _dbContext.Set<TemplateCacheEntity>().Find(dto.TemplateHashKey);
            if (entity == null)
            {
                entity = new TemplateCacheEntity
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
                _dbContext.Set<TemplateCacheEntity>().Add(entity);
            }
            else
            {
                entity.SignalExactMatchPathsJson = dto.SignalExactMatchPathsJson;
                entity.IsExactMatchFullMatch = dto.IsExactMatchFullMatch;
                entity.IsGenericMatchFullMatch = dto.IsGenericMatchFullMatch;
                entity.GenericMatchExpressionJson = dto.GenericMatchExpressionJson;
                entity.InstanceExactMatchExpressionJson = dto.InstanceExactMatchExpressionJson;
                entity.NormalizedMatchExpressionJson = dto.NormalizedMatchExpressionJson;
                entity.AfterMatchAction = dto.AfterMatchAction;
                entity.CancelAction = dto.CancelAction;
                _dbContext.Set<TemplateCacheEntity>().Update(entity);
            }

            _dbContext.SaveChanges();
        }
    }
}
