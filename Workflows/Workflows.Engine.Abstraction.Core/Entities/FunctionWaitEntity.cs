using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.Helpers;
using System.Reflection;
namespace Workflows.Handler.InOuts.Entities
{
    public sealed class WorkflowWaitEntity : WaitEntity
    {
        internal WorkflowWaitEntity()
        {

        }

        //todo:delete this property
        [NotMapped]
        public WaitEntity FirstWait { get;  internal set; }
        internal IAsyncEnumerator<Wait> Runner { get; set; }

        [NotMapped] public MethodInfo WorkflowInfo { get;  internal set; }

        internal override bool IsCompleted() => ChildWaits.Any(x => x.Status == WaitStatus.Waiting) is false;

        internal override void OnAddWait()
        {
            IsRoot = ParentWait == null && ParentWaitId == null;
            base.OnAddWait();
        }
        internal override bool ValidateWaitRequest()
        {
            var hasSubWorkflowAttribute = WorkflowInfo.GetCustomAttributes<SubWorkflowAttribute>().Any();
            if (!hasSubWorkflowAttribute)
                WorkflowInstance.AddLog(
                      $"You didn't set attribute [{nameof(SubWorkflowAttribute)}] for method [{WorkflowInfo.GetFullName()}].",
                      LogType.Error,
                      StatusCodes.WaitValidation);
            return base.ValidateWaitRequest();
        }
    }
}
