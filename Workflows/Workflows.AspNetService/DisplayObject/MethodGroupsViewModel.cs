using Workflows.Handler.InOuts.Entities;
using Workflows.Handler.UiService.InOuts;

namespace Workflows.MvcUi.DisplayObject
{
    public class MethodGroupsViewModel
    {
        public List<MethodGroupInfo> MethodGroups { get; internal set; }
        public int SelectedService { get; internal set; }
        public string SearchTerm { get; internal set; }
        public List<ServiceData> Services { get; internal set; }
    }
}
