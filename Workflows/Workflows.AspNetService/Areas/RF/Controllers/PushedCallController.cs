using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Workflows.Handler.UiService;

namespace Workflows.MvcUi.Areas.RF.Controllers
{
    [Area("RF")]
    public class SignalController : Controller
    {
        private readonly ILogger<SignalController> _logger;
        private readonly IUiService _service;

        public SignalController(ILogger<SignalController> logger, IUiService service)
        {
            _logger = logger;
            _service = service;
        }

        public async Task<IActionResult> Details(int signalId)
        {
            return View(await _service.GetSignalDetails(signalId));
        }
    }
}