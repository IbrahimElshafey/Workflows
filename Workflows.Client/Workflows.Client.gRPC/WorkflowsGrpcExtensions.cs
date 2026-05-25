using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflows.Communication.Abstraction;


namespace Workflows.Client.gRPC
{
    public static class WorkflowsGrpcExtensions
    {
        /// <summary>
        /// Registers the gRPC-based message transport and subscriber.
        /// </summary>
        public static IServiceCollection AddWorkflowsGrpcTransport(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.TryAddSingleton<ITransportFactory, DefaultTransportFactory>();
            services.TryAddSingleton<IMessageDispatcher, DefaultMessageDispatcher>();

            services.AddSingleton<GrpcWorkflowMessageSubscriber>();
            services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<GrpcWorkflowMessageSubscriber>());
            
            // Register as concrete types so DefaultTransportFactory can resolve them directly.
            services.AddTransient<GrpcWorkflowMessageTransport>();

            return services;
        }

        /// <summary>
        /// Maps the gRPC services on the Server (Orchestrator) and Client (Executor) sides.
        /// </summary>
        public static IEndpointRouteBuilder MapWorkflowGrpcServices(this IEndpointRouteBuilder endpoints)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

            // Map server side if available in DI
            endpoints.MapGrpcService<GrpcWorkflowOrchestratorService>();

            // Map client side if available in DI
            endpoints.MapGrpcService<GrpcWorkflowExecutorService>();

            return endpoints;
        }
    }
}
