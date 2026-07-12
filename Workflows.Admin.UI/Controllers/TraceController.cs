using Microsoft.AspNetCore.Mvc;
using Workflows.Admin.UI.Services;

namespace Workflows.Admin.UI.Controllers
{
    [Route("admin/[controller]")]
    public class TraceController : Controller
    {
        private readonly IAdminQueryService _queryService;

        public TraceController(IAdminQueryService queryService)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        }

        [HttpGet("Index/{instanceId:guid}")]
        public async Task<IActionResult> Index(Guid instanceId, CancellationToken ct)
        {
            var model = await _queryService.GetExecutionTraceAsync(instanceId, ct);
            if (model == null) return NotFound();
            return View(model);
        }
    }
}
