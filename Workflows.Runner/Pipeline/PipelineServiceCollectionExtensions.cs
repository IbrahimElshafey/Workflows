using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.Runner;
using Workflows.Runner.Pipeline;

namespace Workflows.Runner
{
    /// <summary>
    /// Extension methods for registering the refactored pipeline components in DI.
    /// </summary>
    public static class PipelineServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the refactored workflow runner pipeline components.
        /// All components are internal and registered as singletons for performance.
        /// </summary>
        public static IServiceCollection AddRefactoredWorkflowPipeline(this IServiceCollection services)
        {
            // Core services - all internal, no interfaces
            services.AddSingleton<WorkflowStateService>();
            services.AddSingleton<EvaluatorFactory>();
            services.AddSingleton<HandlerFactory>();
            services.AddSingleton<CancelHandler>();

            // The refactored runner (can be registered as IWorkflowRunner when ready to switch)
            // For now, register with a different lifetime to allow side-by-side testing
            services.AddScoped<RefactoredWorkflowRunner>();

            return services;
        }

        /// <summary>
        /// Switches to using the refactored workflow runner as the primary IWorkflowRunner.
        /// WARNING: This replaces the existing WorkflowRunner registration.
        /// </summary>
        public static IServiceCollection UseRefactoredWorkflowRunner(this IServiceCollection services)
        {
            // Replace the IWorkflowRunner registration
            // Note: This assumes the original runner is registered as AddScoped<IWorkflowRunner, WorkflowRunner>
            // You may need to adjust based on actual registration
            services.AddScoped<IWorkflowRunner>(sp => sp.GetRequiredService<RefactoredWorkflowRunner>());

            return services;
        }
    }
}
