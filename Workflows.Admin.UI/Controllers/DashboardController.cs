using Microsoft.AspNetCore.Mvc;
using Workflows.Admin.UI.Services;

namespace Workflows.Admin.UI.Controllers
{
    [Route("admin/[controller]")]
    public class DashboardController : Controller
    {
        private readonly IAdminQueryService _queryService;

        public DashboardController(IAdminQueryService queryService)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var model = await _queryService.GetDashboardMetricsAsync(ct);
            return View(model);
        }
    }
}
