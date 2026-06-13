using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Communication.Abstraction;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Orchestrator;
using Workflows.Runner;

namespace Workflows.Hosting.InProcess
{
    public static class InProcessHost
    {
        public static IServiceCollection AddWorkflowsInProcessHost(this IServiceCollection services, string connectionString)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            // 1. EF Core Database Context & Persistence Store Registrations (using new Sqlite adapter)
            services.AddWorkflowsSqlite(connectionString);

            // 3. Orchestrator & Background Scheduler
            services.AddSingleton<Scheduler>();
            services.AddSingleton<IExternalScheduler>(sp => sp.GetRequiredService<Scheduler>());
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<Scheduler>());

            services.AddSingleton<WorkflowExecutionChannel>();
            services.AddSingleton<InboxOptions>();
            services.AddScoped<WorkflowExecutionSession>();
            services.AddScoped<WorkflowRunnerClient>(); // Direct DB writer used by Commit Worker
            services.AddScoped<IWorkflowRunnerClient, ChannelWorkflowRunnerClient>(); // Proxied client for the Runner

            services.AddHostedService<RunnerWorker>();
            services.AddHostedService<CoordinatorCommitWorker>();
            services.AddHostedService<OutboxSweeperWorker>();
            services.AddHostedService<InboxPollerWorker>();

            services.AddScoped<CommandResultInboxWriter>();

            services.AddScoped<Workflows.Orchestrator.IWorkflowCloner, Workflows.Orchestrator.WorkflowCloner>();
            services.AddScoped<Workflows.Orchestrator.ISignalPreFilter, Workflows.Orchestrator.SignalPreFilter>();
            services.AddScoped<Workflows.Orchestrator.Orchestrator>();
            services.AddScoped<IOrchestrator, InboxOrchestrator>();

            // 4. In-Process Message Transport & Routing Setup
            services.AddSingleton<InProcessMessageTransport>();
            services.AddSingleton<InProcessMessageSubscriber>();

            var routingBuilder = new TransportRoutingBuilder();
            routingBuilder.UseDefault<InProcessMessageTransport, InProcessMessageSubscriber>();
            
            // Explicitly configure rules for runner requests to go through our in-process loopback transport
            routingBuilder.ForMessage<StartWorkflowRequest>()
                .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");
            routingBuilder.ForMessage<WorkflowExecutionRequest>()
                .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");

            services.AddSingleton(routingBuilder);
            services.AddSingleton<ITransportFactory, DefaultTransportFactory>();
            services.AddSingleton<IMessageDispatcher, DefaultMessageDispatcher>();

            // 5. Runner Proxy (resolves IWorkflowRunner to RunnerProxy on the Orchestrator side)
            services.AddScoped<IWorkflowRunner, RunnerProxy>();

            return services;
        }
    }
}
