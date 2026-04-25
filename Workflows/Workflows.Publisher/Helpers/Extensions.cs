using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workflows.Publisher.Abstraction;
using Workflows.Publisher.Implementation;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Workflows.Publisher.Helpers;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Helpers
{
    public static class Extensions
    {
        private static IServiceProvider _serviceProvider;

        public static void AddWorkflowsPublisher(this IServiceCollection services, Abstraction.ISenderSettings settings)
        {
            services.AddSingleton<Abstraction.IFailedRequestHandler, Implementation.FailedRequestHandler>();
            //services.AddSingleton<IFailedRequestRepo, InMemoryFailedRequestRepo>();
            services.AddSingleton<Abstraction.IFailedRequestStore, Implementation.OnDiskFailedRequestRepo>();
            services.AddSingleton(typeof(Abstraction.ISenderSettings), settings);
            services.AddHttpClient();
            services.AddSingleton(typeof(Abstraction.ISignalSender), settings.SignalSenderType);
        }

        public static void UseWorkflowsPublisher(this IHost app)
        {
            _serviceProvider = app.Services;
            var failedRequestsHandler = app.Services.GetService<Abstraction.IFailedRequestHandler>();
            failedRequestsHandler.HandleFailedRequests();
        }

        public static object GetInstance(Type type)
        {
            if (_serviceProvider == null) return null;
            return _serviceProvider.GetService(type) ??
                ActivatorUtilities.CreateInstance(_serviceProvider, type);
        }

        public static bool IsAsyncMethod(this MethodBase method)
        {
            var asyncAttr = method.GetCustomAttribute(typeof(AsyncStateMachineAttribute));

            if (asyncAttr == null)
            {
                return
                  asyncAttr == null &&
                  method is MethodInfo mi &&
                  mi != null &&
                  mi.ReturnType.IsGenericType &&
                  mi.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);
            }
            return true;
        }


        public static string GetFullName(this MethodBase method)
        {
            return $"{method.DeclaringType.FullName}.{method.Name}";
        }
    }
}