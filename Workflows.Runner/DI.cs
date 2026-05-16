using Microsoft.Extensions.DependencyInjection;
using System;
using Workflows.Abstraction.Runner;
using Workflows.Definition.Registration;
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

        public static IServiceCollection AddImmediateCommand<TCommand, TResult, THandler>(
            this IServiceCollection services)
            where THandler : class, IImmediateCommandHandler<TCommand, TResult>
        {
            services.AddTransient<IImmediateCommandHandler<TCommand, TResult>, THandler>();
            return services;
        }

        public static IServiceCollection AddDeferredCommand<TCommand, TDispatcher>(this IServiceCollection services)
            where TDispatcher : class, IDeferredCommandDispatcher<TCommand>
        {
            services.AddTransient<IDeferredCommandDispatcher<TCommand>, TDispatcher>();
            return services;
        }

     
        public static IServiceCollection AddDefaultDeferredDispatcher(this IServiceCollection services, Type deferredDispatcherType)
        {
            services.AddTransient(typeof(IDeferredCommandDispatcher<>), deferredDispatcherType);
            return services;
        }

        public static IServiceCollection AddDefaultImmediateCommandHandler(this IServiceCollection services, Type immediateCommandHandler)
        {
            services.AddTransient(typeof(IImmediateCommandHandler<,>), immediateCommandHandler);
            return services;
        }
    }
}
