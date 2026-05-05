using Microsoft.Extensions.DependencyInjection;
using Workflows.Definition;
using Workflows.Runner.Cache;
using Workflows.Runner.ExpressionTransformers;
using Workflows.Runner.Helpers;

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
            //services.AddScoped<IWorkflowRunner, WorkflowRunner>();
            services.AddSingleton<MatchExpressionTransformer>();
            services.AddSingleton<StateMachineAdvancer>();
            services.AddSingleton<IDelegateSerializer, DelegateSerializer>();
            services.AddSingleton<IClosureContextResolver, ClosureContextResolver>();
            services.AddSingleton<IWorkflowBuilder, WorkflowBuilder>();
            services.AddSingleton<IWorkflowRegistry, WorkflowBuilder>();
            services.AddSingleton<WorkflowTemplateCache>();
            services.AddSingleton<Mapper>();
            return services;
        }
    }
}
