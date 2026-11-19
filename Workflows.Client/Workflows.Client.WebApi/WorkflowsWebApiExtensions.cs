using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Orchestrator;
using Workflows.Communication.Abstraction;


namespace Workflows.Client.WebApi
{
    public static class WorkflowsWebApiExtensions
    {
        /// <summary>
        /// Registers the HTTP-based message transport and subscriber.
        /// </summary>
        public static IServiceCollection AddWorkflowsHttpTransport(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.TryAddSingleton<ITransportFactory, DefaultTransportFactory>();
            services.TryAddSingleton<IMessageDispatcher, DefaultMessageDispatcher>();

            services.AddHttpClient<HttpWorkflowMessageTransport>();
            services.AddSingleton<HttpWorkflowMessageSubscriber>();
            services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<HttpWorkflowMessageSubscriber>());
            
            // Register as concrete types so DefaultTransportFactory can resolve them directly.
            services.AddTransient<HttpWorkflowMessageTransport>(sp => 
                ActivatorUtilities.CreateInstance<HttpWorkflowMessageTransport>(sp));

            return services;
        }

        /// <summary>
        /// Maps the Web API endpoints on the Server (Orchestrator) side.
        /// </summary>
        public static IEndpointRouteBuilder MapWorkflowOrchestratorEndpoints(this IEndpointRouteBuilder endpoints)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

            endpoints.MapPost("/api/workflows/signal", async (
                HttpContext context,
                IOrchestrator orchestrator,
                IObjectSerializer serializer) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                var signalDto = serializer.Deserialize<SignalDto>(body);
                await orchestrator.ProcessSignalAsync(signalDto).ConfigureAwait(false);
                return Results.Ok();
            });

            endpoints.MapPost("/api/workflows/command-result", async (
                HttpContext context,
                IOrchestrator orchestrator,
                IObjectSerializer serializer) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                var resultDto = serializer.Deserialize<CommandResultDto>(body);
                await orchestrator.ProcessCommandResultAsync(resultDto).ConfigureAwait(false);
                return Results.Ok();
            });

            endpoints.MapPost("/api/workflows/start", async (
                HttpContext context,
                IOrchestrator orchestrator,
                IObjectSerializer serializer) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                var startReq = serializer.Deserialize<StartWorkflowRequest>(body);
                var workflowId = await orchestrator.StartWorkflowAsync(
                    startReq.WorkflowName, 
                    1, // Default version
                    startReq.Input
                ).ConfigureAwait(false);

                // Build AsyncResult matching what orchestrator / runner returns
                var asyncResult = new AsyncResult(
                    workflowId, 
                    new object(), 
                    "Accepted", 
                    "Workflow started successfully via Web API", 
                    DateTime.UtcNow
                );

                var responsePayload = serializer.Serialize(asyncResult);
                return Results.Text(
                    responsePayload as string ?? responsePayload?.ToString() ?? string.Empty, 
                    "application/json"
                );
            });

            return endpoints;
        }

        /// <summary>
        /// Maps the Web API endpoints on the Client (Executor) side.
        /// </summary>
        public static IEndpointRouteBuilder MapWorkflowClientEndpoints(this IEndpointRouteBuilder endpoints)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

            endpoints.MapPost("/api/workflows/command-dispatch", async (
                HttpContext context,
                HttpWorkflowMessageSubscriber subscriber,
                IObjectSerializer serializer) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                var notification = serializer.Deserialize<CommandDispatchNotification>(body);
                await subscriber.HandleMessageAsync(typeof(CommandDispatchNotification), notification).ConfigureAwait(false);
                return Results.Ok();
            });

            return endpoints;
        }
    }
}
