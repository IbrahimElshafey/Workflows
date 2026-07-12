using Microsoft.AspNetCore.Mvc;
using Workflows.Admin.UI.Services;

namespace Workflows.Admin.UI.Controllers
{
    [Route("admin/[controller]")]
    public class DefinitionsController : Controller
    {
        private readonly IAdminQueryService _queryService;

        public DefinitionsController(IAdminQueryService queryService)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var model = await _queryService.GetDefinitionsAsync(ct);
            return View(model);
        }

        [HttpGet("Detail/{name}/{version:int}")]
        public async Task<IActionResult> Detail(string name, int version, CancellationToken ct)
        {
            var model = await _queryService.GetDefinitionDetailAsync(name, version, ct);
            if (model == null) return NotFound();
            return View(model);
        }
    }
}
