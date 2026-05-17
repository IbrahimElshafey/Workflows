using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Workflows.Orchestrator.Data.EF
{
    public static class DI
    {
        public static IServiceCollection AddWorkflowsEFStore(this IServiceCollection services, string connectionString)
        {
            services.AddDbContext<WorkflowsDbContext>(options =>
                options.UseSqlServer(connectionString)); // or UseNpgsql depending on target

            services.AddScoped<IWorkflowStore, EFWorkflowStore>();
            return services;
        }
    }
}
