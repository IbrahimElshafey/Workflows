using Microsoft.Extensions.DependencyInjection;
using System;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Runner;
using Workflows.Definition.Registration;
using Workflows.Runner.ExpressionTransformers;
using Workflows.Runner.Helpers;
using Workflows.Runner.Pipeline;
using Workflows.Runner.Pipeline.Matchers;
using Workflows.Runner.Pipeline.Processors;

namespace Workflows.Runner
{
    public static class DI
    {
        public static IServiceCollection AddWorkflowsRunner(this IServiceCollection services)
        {
            // Core services - all internal, no interfaces
            services.AddSingleton<IWorkflowHydrator, WorkflowHydrator>();
            services.AddScoped<WorkflowStateService>();
            services.AddSingleton<CallbackRegistry>();
            services.AddSingleton<ICallbackRegistry>(sp => sp.GetRequiredService<CallbackRegistry>());
            services.AddScoped<MatcherFactory>();
            services.AddScoped<ProcessorFactory>();
            services.AddSingleton<CancelProcessor>();
            services.AddSingleton<StateMachineAdvancer>();
            services.AddScoped<Mapper>();

            services.AddScoped<SignalWaitMatcher>();
            services.AddScoped<TimeWaitMatcher>();
            services.AddScoped<DeferredCommandMatcher>();
            services.AddScoped<GroupWaitMatcher>();
            services.AddScoped<SubWorkflowWaitMatcher>();
            services.AddScoped<WorkflowExecutionContext>();
            services.AddScoped<StateMachineAdvancer>();
            services.AddScoped<IWorkflowRunner, WorkflowRunner>();
           
            // The refactored runner (can be registered as IWorkflowRunner when ready to switch)
            // For now, register with a different lifetime to allow side-by-side testing
            services.AddScoped<WorkflowRunner>();
            /*to add
             * RunWorkflowSettings settings,
            IWorkflowRunResultSender runResultSender,
            */
            //services.AddScoped<IWorkflowRunner, WorkflowRunner>();
            services.AddSingleton<MatchExpressionTransformer>();
            services.AddSingleton<WorkflowBuilder>();

            // Default ICommandHandlerFactory — resolves handlers by key from DI
            services.AddSingleton<ICommandHandlerFactory, DiCommandHandlerFactory>();
            services.AddSingleton<IWorkflowBuilder>(sp => sp.GetRequiredService<WorkflowBuilder>());
            services.AddSingleton<IWorkflowRegistry>(sp => sp.GetRequiredService<WorkflowBuilder>());
            services.AddScoped<Mapper>();
            return services;
        }

        public static IServiceCollection AddImmediateCommand<TCommand, TResult, THandler>(
            this IServiceCollection services)
            where THandler : class, IImmediateCommandHandler<TCommand, TResult>
        {
            services.AddTransient<IImmediateCommandHandler<TCommand, TResult>, THandler>();
            return services;
        }

        /// <summary>
        /// Registers a keyed immediate command handler so <see cref="DiCommandHandlerFactory"/>
        /// can resolve it by <paramref name="handlerKey"/> at runtime.
        /// </summary>
        public static IServiceCollection AddImmediateCommand<TCommand, TResult, THandler>(
            this IServiceCollection services,
            string handlerKey)
            where THandler : class, IImmediateCommandHandler<TCommand, TResult>
        {
            services.AddTransient<IImmediateCommandHandler<TCommand, TResult>, THandler>();
            services.AddSingleton(new HandlerKeyEntry(handlerKey, typeof(TCommand), typeof(TResult)));
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
