using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Workflows.Admin.UI.Services;

namespace Workflows.Admin.UI.Controllers
{
    [Route("admin/[controller]")]
    public class ActionsController : Controller
    {
        private readonly IAdminCommandService _commandService;
        private readonly WorkflowsAdminUIOptions _options;

        public ActionsController(
            IAdminCommandService commandService,
            IOptions<WorkflowsAdminUIOptions> options)
        {
            _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        private bool ActionsEnabled => _options.EnableWriteActions && _commandService.CanExecuteActions;

        [HttpPost("Start")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(string workflowName, int version, string? inputJson)
        {
            if (!ActionsEnabled) return Forbid();

            object? input = null;
            if (!string.IsNullOrWhiteSpace(inputJson))
            {
                try
                {
                    input = JsonConvert.DeserializeObject(inputJson);
                }
                catch
                {
                    TempData["Error"] = "Invalid JSON input payload.";
                    return RedirectToAction("Index", "Instances");
                }
            }

            var instanceId = await _commandService.StartInstanceAsync(workflowName, version, input);
            TempData["Success"] = $"Started workflow instance {instanceId}.";
            return RedirectToAction("Detail", "Instances", new { id = instanceId });
        }

        [HttpPost("Cancel/{instanceId:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(Guid instanceId, string token, string? reason)
        {
            if (!ActionsEnabled) return Forbid();

            await _commandService.CancelInstanceAsync(instanceId, token, reason);
            TempData["Success"] = "Cancellation token triggered.";
            return RedirectToAction("Detail", "Instances", new { id = instanceId });
        }

        [HttpPost("Signal/{instanceId:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Signal(Guid instanceId, string signalIdentifier, string? payloadJson)
        {
            if (!ActionsEnabled) return Forbid();

            object? payload = null;
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                try
                {
                    payload = JsonConvert.DeserializeObject(payloadJson);
                }
                catch
                {
                    TempData["Error"] = "Invalid JSON signal payload.";
                    return RedirectToAction("Detail", "Instances", new { id = instanceId });
                }
            }

            await _commandService.DispatchSignalAsync(instanceId, signalIdentifier, payload);
            TempData["Success"] = $"Signal '{signalIdentifier}' dispatched.";
            return RedirectToAction("Detail", "Instances", new { id = instanceId });
        }

        [HttpPost("Compensate/{instanceId:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Compensate(Guid instanceId, string token)
        {
            if (!ActionsEnabled) return Forbid();

            await _commandService.ForceCompensateAsync(instanceId, token);
            TempData["Success"] = "Compensation triggered.";
            return RedirectToAction("Detail", "Instances", new { id = instanceId });
        }

        [HttpPost("Terminate/{instanceId:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Terminate(Guid instanceId)
        {
            if (!ActionsEnabled) return Forbid();

            await _commandService.TerminateInstanceAsync(instanceId);
            TempData["Success"] = "Instance terminated.";
            return RedirectToAction("Detail", "Instances", new { id = instanceId });
        }
    }
}
