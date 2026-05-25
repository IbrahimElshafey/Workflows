using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Storage.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class PostgresServiceCollectionExtensions
    {
        public static IServiceCollection AddWorkflowsPostgres(this IServiceCollection services, string connectionString)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            services.AddWorkflowsEntityFrameworkCore();

            services.AddDbContext<WorkflowsDbContext>((sp, options) =>
            {
                var interceptor = sp.GetRequiredService<WorkflowAuditingInterceptor>();
                options.UseNpgsql(connectionString)
                       .AddInterceptors(interceptor);
            });

            return services;
        }
    }
}
