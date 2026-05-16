using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Runner.Cache;

namespace Workflows.Runner.Pipeline.Evaluators
{
    /// <summary>
    /// Evaluates deferred command results on integration callback return.
    /// Maps the result to the target CommandWait and runs the OnResultAction lambda to update variables.
    /// </summary>
    internal class DeferredCommandEvaluator : WorkflowWaitEvaluator
    {
        private readonly WorkflowTemplateCache _templateCache;

        public DeferredCommandEvaluator(WorkflowTemplateCache templateCache)
        {
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
        }

        public override async Task<bool> EvaluateAsync(WorkflowExecutionContext context)
        {
            var commandWaitDto = context.TriggeringWaitDto as CommandWaitDto;
            if (commandWaitDto == null)
            {
                throw new InvalidOperationException("DeferredCommandEvaluator requires a CommandWaitDto.");
            }

            var commandWait = context.TriggeringWait as Definition.ICommandWait;
            if (commandWait == null)
            {
                throw new InvalidOperationException("Triggering wait could not be mapped to ICommandWait.");
            }

            var result = context.IncomingRequest.CommandResult;

            // Get command wait properties via reflection
            var commandWaitType = commandWait.GetType();
            var onResultActionProperty = commandWaitType.GetProperty("OnResultAction", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var onFailureActionProperty = commandWaitType.GetProperty("OnFailureAction", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var explicitStateProperty = commandWaitType.BaseType.GetProperty("ExplicitState", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            var onResultAction = onResultActionProperty?.GetValue(commandWait);
            var onFailureAction = onFailureActionProperty?.GetValue(commandWait);
            var explicitState = explicitStateProperty?.GetValue(commandWait);

            // TODO: Handle failure scenarios
            // For now, assume success and invoke OnResultAction

            if (onResultAction != null)
            {
                InvokeOnResultAction(onResultAction, result, explicitState);
            }

            return true;
        }

        private void InvokeOnResultAction(object action, object result, object explicitState)
        {
            var invoker = _templateCache.GetOrAddOnResultInvoker(action.GetType());
            if (invoker == null)
            {
                throw new InvalidOperationException("OnResultAction signature is not supported or Invoke method not found.");
            }

            invoker(action, result, explicitState);
        }
    }
}
