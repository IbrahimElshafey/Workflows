using Workflows.Handler.InOuts.Entities;
using Workflows.Handler.UiService.InOuts;

namespace Workflows.MvcUi.DisplayObject
{
    public class WorkflowsViewModel
    {
        public List<WorkflowInfo> Workflows { get; internal set; }
        public string SearchTerm { get; internal set; }
        public int SelectedService { get; internal set; }
        public List<ServiceData> Services { get; internal set; }
    }
}
