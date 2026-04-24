using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Workflows.Handler.UiService;
using Workflows.MvcUi.DisplayObject;

namespace Workflows.MvcUi.Areas.RF.Controllers
{
    [Area("RF")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUiService _uiService;

        public HomeController(ILogger<HomeController> logger, IUiService uiService)
        {
            _logger = logger;
            _uiService = uiService;
        }

        public IActionResult Index()
        {
            var model = new HomePageModel();
            model.SetMenu();
            return View(model);
        }

        [ActionName(PartialNames.ServicesList)]
        public async Task<IActionResult> ServicesView()
        {
            return PartialView(PartialNames.ServicesList, new ServicesListModel(await _uiService.GetServicesSummary()));
        }


        [ActionName(PartialNames.Signals)]
        public async Task<IActionResult> Signals(int serviceId = -1, string searchTerm = null)
        {
            return PartialView(
                PartialNames.Signals,
                new SignalsViewMode
                {
                    Calls = await _uiService.GetSignals(0, serviceId, searchTerm),
                    Services = await _uiService.GetServices(),
                    SelectedService = serviceId,
                    SearchTerm = searchTerm
                });
        }

        [ActionName(PartialNames.LatestLogs)]
        public async Task<IActionResult> LatestLogs(int serviceId = -1, int statusCode = -1)
        {
            return PartialView(
                PartialNames.LatestLogs,
                new LogsViewModel
                {
                    Logs = await _uiService.GetLogs(0, serviceId, statusCode),
                    Services = await _uiService.GetServices(),
                    SelectedService = serviceId,
                    SelectedStatusCode = statusCode
                });
        }

        [ActionName(PartialNames.Workflows)]
        public async Task<IActionResult> GetWorkflows(
            int serviceId = -1, 
            string searchTerm = null)
        {
            return PartialView(
                PartialNames.Workflows,
                new WorkflowsViewModel
                {
                    Workflows = await _uiService.GetWorkflowsSummary(serviceId, searchTerm),
                    SelectedService = serviceId,
                    SearchTerm = searchTerm,
                    Services = await _uiService.GetServices(),
                });
        }

        [ActionName(PartialNames.MethodGroups)]
        public async Task<IActionResult> GetMethodGroups(int serviceId = -1, string searchTerm = null)
        {
            return PartialView(
                PartialNames.MethodGroups,
                new MethodGroupsViewModel
                {
                    MethodGroups = await _uiService.GetMethodGroupsSummary(serviceId, searchTerm),
                    SelectedService = serviceId,
                    SearchTerm = searchTerm,
                    Services = await _uiService.GetServices(),
                });
        }
    }
}