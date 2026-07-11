using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Runner.ExpressionTransformers;
using Workflows.Runner.Helpers;
using Workflows.Runner.Migration;
using Workflows.Runner.Pipeline;
using Workflows.Runner.Pipeline.CompletionChecker;
using Workflows.Runner.Pipeline.Serializers;

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
            services.AddScoped<CompletionCheckerFactory>();
            services.AddScoped<SerializerFactory>();
            services.AddSingleton<CancelTokensHandler>();
            services.AddSingleton<StateMachineAdvancer>();
            services.AddScoped<Mapper>();
            services.AddScoped<WorkflowRunLoop>();

            services.AddScoped<SignalCompletionChecker>();
            services.AddScoped<TimeWaitMatcher>();
            services.AddScoped<CommandCompletionChecker>();
            services.AddScoped<GroupCompletionChecker>();
            services.AddScoped<WorkflowCompletionChecker>();
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

            // Register CommandRegistryOptions
            services.AddSingleton<CommandRegistryOptions>();

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

        public static IServiceCollection AddSyncCommand<TInput, TOutput, THandler>(
            this IServiceCollection services, string commandKey)
            where THandler : class, ICommandHandler<TInput, TOutput>
        {
            services.AddKeyedTransient<ICommandHandler<TInput, TOutput>, THandler>(commandKey);
            services.AddOrUpdateCommandRegistry(commandKey,
                new CommandMetadata(typeof(TInput), typeof(TOutput), IsAsync: false, IsExternal: false, null));
            return services;
        }

        public static IServiceCollection AddSyncCommand<TInput, TOutput>(
            this IServiceCollection services, string commandKey,
            Func<IServiceProvider, TInput, TOutput> handler)
        {
            services.AddKeyedSingleton(commandKey, (sp, _) => handler);
            services.AddOrUpdateCommandRegistry(commandKey,
                new CommandMetadata(typeof(TInput), typeof(TOutput), IsAsync: false, IsExternal: false, null));
            return services;
        }

        public static IServiceCollection AddStandardAsyncCommand<TInput, TOutput, TDispatcher, TReceiver>(
            this IServiceCollection services, string commandKey)
            where TDispatcher : class, IDispatcher<TInput>
            where TReceiver : class, IReceiver<TOutput>
        {
            services.AddKeyedTransient<IDispatcher<TInput>, TDispatcher>(commandKey);
            services.AddKeyedTransient<IReceiver<TOutput>, TReceiver>(commandKey);
            services.AddOrUpdateCommandRegistry(commandKey,
                new CommandMetadata(typeof(TInput), typeof(TOutput), IsAsync: true, IsExternal: false, null));
            return services;
        }

        public static IServiceCollection AddExternalAsyncCommand<TInput, TOutput, TCallbackPayload, TDispatcher, TReceiver>(
            this IServiceCollection services, string commandKey,
            Expression<Func<TCallbackPayload, TInput, bool>> matchExpression)
            where TDispatcher : class, IDispatcher<TInput>
            where TReceiver : class, IReceiver<TOutput>
        {
            services.AddKeyedTransient<IDispatcher<TInput>, TDispatcher>(commandKey);
            services.AddKeyedTransient<IReceiver<TOutput>, TReceiver>(commandKey);
            services.AddOrUpdateCommandRegistry(commandKey,
                new CommandMetadata(typeof(TInput), typeof(TOutput), IsAsync: true, IsExternal: true, matchExpression));
            return services;
        }

        public static IServiceCollection AddDeferredCommand<TInput, TOutput, TDispatcher, TReceiver>(
            this IServiceCollection services, string commandKey)
            where TInput : class, IDeferredCommand<TInput, TOutput>
            where TDispatcher : class, IDispatcher<TInput>
            where TReceiver : class, IReceiver<TOutput>
        {
            services.AddKeyedTransient<IDispatcher<TInput>, TDispatcher>(commandKey);
            services.AddKeyedTransient<IReceiver<TOutput>, TReceiver>(commandKey);

            LambdaExpression? matchExpr = null;
            try
            {
                var instance = (IDeferredCommand<TInput, TOutput>)Activator.CreateInstance(typeof(TInput))!;
                matchExpr = instance.MatchingFunction;
            }
            catch
            {
                // Fall back if parameterless constructor is not available
            }

            services.AddOrUpdateCommandRegistry(commandKey,
                new CommandMetadata(typeof(TInput), typeof(TOutput), IsAsync: true, IsExternal: true, matchExpr));
            return services;
        }

        private static IServiceCollection AddOrUpdateCommandRegistry(
            this IServiceCollection services, string key, CommandMetadata metadata)
        {
            var descriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(CommandRegistryOptions));

            CommandRegistryOptions opts;
            if (descriptor?.ImplementationInstance is CommandRegistryOptions existing)
            {
                opts = existing;
            }
            else
            {
                opts = new CommandRegistryOptions();
                services.AddSingleton(opts);
            }

            opts.Commands[key] = metadata;
            return services;
        }

        /// <summary>
        /// Registers a migration class for a specific version transition.
        /// The migration is keyed as "{WorkflowName}:{fromVersion}:{toVersion}" for resolution by WorkflowVersionRouter.
        /// </summary>
        public static IServiceCollection AddWorkflowMigration<TOld, TNew, TMigration>(
            this IServiceCollection services)
            where TOld : WorkflowStateWrapper
            where TNew : WorkflowStateWrapper
            where TMigration : WorkflowMigration<TOld, TNew>, new()
        {
            var attr = typeof(TMigration).GetCustomAttribute<WorkflowMigrationAttribute>()
                ?? throw new InvalidOperationException(
                    $"{typeof(TMigration).Name} must be decorated with [WorkflowMigration].");

            string key = $"{attr.WorkflowName}:{attr.FromVersion}:{attr.ToVersion}";

            services.AddKeyedTransient<IWorkflowMigrationExecutor>(key,
                (sp, _) => ActivatorUtilities.CreateInstance<
                    WorkflowMigrationExecutor<TOld, TNew, TMigration>>(sp));

            return services;
        }
    }
}
