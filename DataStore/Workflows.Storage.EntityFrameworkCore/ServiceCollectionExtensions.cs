using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            services.AddScoped<IInstanceLockManager, InstanceLockManager>();
            services.AddSingleton<WorkflowAuditingInterceptor>();

            // No-op DLQ publisher so the compensation worker can always resolve IDlqPublisher.
            // TryAdd semantics keep any application-supplied publisher registered earlier.
            services.TryAddSingleton<IDlqPublisher, NoOpDlqPublisher>();
            return services;
        }
    }
}
