using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.Runner;
using Workflows.Runner.ExpressionTransformers;

namespace Workflows.Runner
{
    public static class DI
    {
        public static IServiceCollection AddWorkflowsRunner(this IServiceCollection services)
        {
            /*to add
             * RunWorkflowSettings settings,
            IWorkflowRunResultSender runResultSender,
            */
            services.AddScoped<IWorkflowRunner, WorkflowRunner>();
            services.AddSingleton<MatchExpressionTransformer>();
            services.AddSingleton<MatchExpressionCache>();
            services.AddSingleton<IExpressionSerializer, ExpressionSerializer>();
            return services;
        }
    }
}
