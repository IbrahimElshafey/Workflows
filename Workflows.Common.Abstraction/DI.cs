using Microsoft.Extensions.DependencyInjection;
using Workflows.Shared.Communication;
using Workflows.Shared.Serialization;

namespace Workflows.Shared
{
    public static class DI
    {
        public static IServiceCollection AddWorkflowsShared(this IServiceCollection services)
        {
            services.AddSingleton<IObjectSerializer, JsonObjectSerializer>();
            services.AddSingleton<IExpressionSerializer, ExpressionSerializer>();
            //services.AddSingleton(sp =>
            //{
            //    var builder = new TransportRoutingBuilder();

            //    // The Fallback
            //    builder.UseDefault<HttpTransport, HttpSubscriber>();

            //    // Route based on Type AND input values using Expression Trees
            //    builder.ForMessage<WorkflowExecutionRequest>()
            //           .When(req => req.Context.WorkflowTypeName == "OrderWorkflow")
            //           .Use<RabbitMqTransport, RabbitMqSubscriber>();

            //    // Route purely based on Type
            //    builder.ForMessage<BulkRegistrationPackage>()
            //           .Use<GrpcTransport, GrpcSubscriber>();

            //    return builder;
            //});

            services.AddSingleton<ITransportFactory, DefaultTransportFactory>();

            //// Register the actual transport implementations so the Factory can resolve them
            //services.AddTransient<HttpTransport>();
            //services.AddTransient<HttpSubscriber>();
            //services.AddTransient<RabbitMqTransport>();
            //services.AddTransient<RabbitMqSubscriber>();
            //services.AddTransient<GrpcTransport>();
            //services.AddTransient<GrpcSubscriber>();
            return services;
        }
    }
}
