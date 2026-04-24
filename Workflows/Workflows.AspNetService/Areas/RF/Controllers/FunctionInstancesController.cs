using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Workflows.Handler.UiService;
using Workflows.MvcUi.DisplayObject;

namespace Workflows.MvcUi.Areas.RF.Controllers
{
    [Area("RF")]
    public class WorkflowInstancesController : Controller
    {
        private readonly ILogger<WorkflowInstancesController> _logger;
        private readonly IUiService _uiService;

        public WorkflowInstancesController(ILogger<WorkflowInstancesController> logger, IUiService uiService)
        {
            _logger = logger;
            _uiService = uiService;
        }
        public async Task<IActionResult> AllInstances(int workflowId, string workflowName)
        {
            return View(
                new WorkflowInstancesModel
                {
                    WorkflowName = workflowName,
                    Instances = await _uiService.GetWorkflowInstances(workflowId)
                });
        }

        public async Task<IActionResult> WorkflowInstance(int instanceId)
        {
            return View(await _uiService.GetWorkflowInstanceDetails(instanceId));
        }

    }
}