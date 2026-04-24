using AspectInjector.Broker;
using Microsoft.Extensions.Logging;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Reflection;

using System;using System.Threading.Tasks; namespace Workflows.Handler.Attributes
{
    [Aspect(Scope.PerInstance, Factory = typeof(HangfireActivator))]
    public class EmitSignalAspect
    {
        private SignalEntity _signal;
        private readonly ISignalDispatcher _callPusher;
        private readonly ILogger<EmitSignalAspect> _logger;
        public EmitSignalAspect(ISignalDispatcher callPusher, ILogger<EmitSignalAspect> logger)
        {
            _callPusher = callPusher;
            _logger = logger;
        }

        [Advice(Kind.Before)]
        public void OnEntry(
            [Argument(Source.Arguments)] object[] args,
            [Argument(Source.Metadata)] MethodBase metadata,
            [Argument(Source.Triggers)] Attribute[] triggers
            )
        {
            var pushResultAttribute = triggers.OfType<EmitSignalAttribute>().First();

            if (string.IsNullOrWhiteSpace(pushResultAttribute.MethodUrn))
                throw new Exception(
                        $"For method [{metadata.GetFullName()}] MethodUrn must not be empty for attribute [{nameof(EmitSignalAttribute)}]");
            if (args.Length > 1)
                throw new Exception(
                    $"You can't apply attribute [{nameof(EmitSignalAttribute)}] to method " +
                    $"[{metadata.GetFullName()}] since it takes more than one parameter.");
            if (metadata is MethodInfo mi && mi.ReturnType == typeof(void))
                throw new Exception(
                    $"You can't apply attribute [{nameof(EmitSignalAttribute)}] to method " +
                    $"[{metadata.GetFullName()}] since return type is void, you can change it to object and return null.");
            _signal = new SignalEntity
            {
                MethodData = new MethodData(metadata as MethodInfo)
                {
                    MethodUrn = pushResultAttribute.MethodUrn,
                    CanPublishFromExternal = pushResultAttribute.FromExternal,
                    IsLocalOnly = pushResultAttribute.IsLocalOnly,
                },
            };
            if (args.Length > 0)
                _signal.Data.Input = args[0];

        }

        [Advice(Kind.After)]
        public void OnExit(
           [Argument(Source.Name)] string name,
           [Argument(Source.ReturnValue)] object result
           )
        {
            try
            {
                _signal.Data.Output = result;
                _callPusher.EnqueueLocalSignalWork(_signal).Wait();//local push in RF shared group
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error when try to push call for method [{name}]");
            }
        }
    }
}