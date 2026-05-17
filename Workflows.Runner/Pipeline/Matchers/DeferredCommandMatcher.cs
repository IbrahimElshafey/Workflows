using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;

namespace Workflows.Runner.Pipeline.Matchers
{
    /// <summary>
    /// Evaluates deferred command results on integration callback return.
    /// Works purely with DTOs - no Wait object conversion needed.
    /// </summary>
    internal class DeferredCommandMatcher : WorkflowWaitMatcher
    {
        private readonly WorkflowExecutionContext _context;
        private readonly MatcherFactory _matcherFactory;

        public DeferredCommandMatcher(WorkflowExecutionContext context, MatcherFactory matcherFactory)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _matcherFactory = matcherFactory ?? throw new ArgumentNullException(nameof(matcherFactory));
        }

        public override async Task<bool> MatchAsync(WaitInfrastructureDto waitDto)
        {
            var commandWaitDto = waitDto as CommandWaitDto;
            if (commandWaitDto == null)
            {
                throw new InvalidOperationException("DeferredCommandMatcher requires a CommandWaitDto.");
            }

            var result = _context.CommandResult;

            // Handle failure scenarios
            if (result is Exception exception)
            {
                // Result is an exception - log or handle failure
                if (_context.WorkflowInstance != null)
                {
                    await _context.WorkflowInstance.OnError(
                        $"Deferred command failed: {exception.Message}", exception);
                }

                // TODO: Invoke OnFailureAction if we store it in DTO
            }
            else
            {
                // Success - TODO: invoke OnResultAction if we store it in DTO
            }

            // Mark this wait as completed
            commandWaitDto.Status = WaitStatus.Completed;

            // Propagate matching to parent wait if present
            if (commandWaitDto.ParentWaitId.HasValue)
            {
                return await MatchParentAsync(commandWaitDto.ParentWaitId.Value, _context, _matcherFactory);
            }

            return true;
        }
    }
}

