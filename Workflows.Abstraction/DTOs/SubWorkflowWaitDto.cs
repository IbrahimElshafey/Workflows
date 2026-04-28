using Workflows.Handler.Helpers;
using System.Reflection;
namespace Workflows.Abstraction.DTOs
{
    public sealed class SubWorkflowWaitDto : WaitBaseDto
    {
        internal SubWorkflowWaitDto()
        {

        }

        

        //internal override bool IsCompleted() => ChildWaits.Any(x => x.Status == WaitStatus.Waiting) is false;

        //internal override void OnAddWait()
        //{
        //    IsRoot = ParentWait == null && ParentWaitId == null;
        //    base.OnAddWait();
        //}
        //internal override bool ValidateWaitRequest()
        //{
        //    var hasSubWorkflowAttribute = WorkflowInfo.GetCustomAttributes<SubWorkflowAttribute>().Any();
        //    if (!hasSubWorkflowAttribute)
        //        WorkflowInstance.AddLog(
        //              $"You didn't set attribute [{nameof(SubWorkflowAttribute)}] for method [{WorkflowInfo.GetFullName()}].",
        //              LogType.Error,
        //              StatusCodes.WaitValidation);
        //    return base.ValidateWaitRequest();
        //}
    }
}
