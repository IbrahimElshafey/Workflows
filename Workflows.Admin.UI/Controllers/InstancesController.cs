using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Workflows.Admin.UI.Models;
using Workflows.Admin.UI.Services;

namespace Workflows.Admin.UI.Controllers
{
    [Route("admin/[controller]")]
    public class InstancesController : Controller
    {
        private readonly IAdminQueryService _queryService;
        private readonly IAdminCommandService _commandService;
        private readonly WorkflowsAdminUIOptions _options;

        public InstancesController(
            IAdminQueryService queryService,
            IAdminCommandService commandService,
            IOptions<WorkflowsAdminUIOptions> options)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
            _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(InstanceFilterViewModel filter, CancellationToken ct)
        {
            var model = await _queryService.QueryInstancesAsync(filter, _options.PageSize, _options.MaxPageSize, ct);
            return View(model);
        }

        [HttpGet("Detail/{id:guid}")]
        public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
        {
            var canExecuteActions = _options.EnableWriteActions && _commandService.CanExecuteActions;
            var model = await _queryService.GetInstanceDetailAsync(id, canExecuteActions, ct);
            if (model == null) return NotFound();
            return View(model);
        }
    }
}
