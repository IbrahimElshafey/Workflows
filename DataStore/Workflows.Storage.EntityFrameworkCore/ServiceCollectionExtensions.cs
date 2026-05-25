using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.Persistence;

namespace Workflows.Storage.EntityFrameworkCore
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddWorkflowsEntityFrameworkCore(this IServiceCollection services)
        {
            services.AddScoped<IWorkflowStore, WorkflowStore>();
            services.AddScoped<IDefinitionRepository, DefinitionRepository>();
            services.AddScoped<ITemplateRepository, TemplateRepository>();
            services.AddSingleton<WorkflowAuditingInterceptor>();
            return services;
        }
    }
}
