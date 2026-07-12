using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workflows.Abstraction.Orchestrator;
using Workflows.Admin.UI.Services;
using Workflows.Storage.EntityFrameworkCore;

namespace Workflows.Admin.UI.Extensions
{
    public static class AdminUIServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the Workflows Admin UI module to the ASP.NET Core application.
        /// The module registers its own read-only <see cref="WorkflowsDbContext"/> using the provided connection string.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="connectionString">Connection string to the workflows database.</param>
        /// <param name="configureOptions">Optional configuration action.</param>
        public static IServiceCollection AddWorkflowsAdminUI(
            this IServiceCollection services,
            string connectionString,
            Action<WorkflowsAdminUIOptions>? configureOptions = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            var options = new WorkflowsAdminUIOptions();
            configureOptions?.Invoke(options);
            services.AddSingleton(Options.Create(options));

            // Register the EF Core store services (read-only usage)
            services.AddWorkflowsEntityFrameworkCore();

            // Register a dedicated DbContext for the admin UI
            services.AddDbContext<WorkflowsDbContext>((sp, dbOptions) =>
            {
                var interceptor = sp.GetRequiredService<WorkflowAuditingInterceptor>();
                switch (options.Provider)
                {
                    case AdminDbProvider.Sqlite:
                        dbOptions.UseSqlite(connectionString).AddInterceptors(interceptor);
                        break;
                    case AdminDbProvider.SqlServer:
                        dbOptions.UseSqlServer(connectionString).AddInterceptors(interceptor);
                        break;
                    case AdminDbProvider.Postgres:
                        dbOptions.UseNpgsql(connectionString).AddInterceptors(interceptor);
                        break;
                    default:
                        throw new NotSupportedException($"Provider '{options.Provider}' is not supported.");
                }
            });

            // Admin services
            services.AddScoped<IAdminQueryService, AdminQueryService>();
            services.AddScoped<IAdminCommandService, AdminCommandService>();
            services.AddScoped<ITopologyExtractorService, TopologyExtractorService>();
            services.AddScoped<ITraceAggregatorService, TraceAggregatorService>();
            services.AddScoped<IMetricsAggregatorService, MetricsAggregatorService>();

            // MVC controllers and Razor views from this assembly
            services
                .AddControllersWithViews()
                .AddApplicationPart(typeof(AdminUIServiceCollectionExtensions).Assembly);

            return services;
        }
    }
}