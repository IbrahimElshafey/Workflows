using System.Linq;
using Workflows.Abstraction.Enums;
using Workflows.Handler.Helpers;

namespace Workflows.Abstraction.DTOs
{
    public class WaitsGroupDto : WaitBaseDto
    {

        internal WaitsGroupDto()
        {
            WaitType = WaitType.GroupWaitAll;
        }

        public string MatchFuncName { get; internal set; }

        //internal override bool IsCompleted()
        //{
        //    var completed = false;
        //    switch (WaitType)
        //    {
        //        case WaitType.GroupWaitAll:
        //            completed = ChildWaits?.All(x => x.Status == WaitStatus.Completed) is true;
        //            break;

        //        case WaitType.GroupWaitFirst:
        //            completed = ChildWaits?.Any(x => x.Status == WaitStatus.Completed) is true;
        //            break;

        //        case WaitType.GroupWaitWithExpression when GroupMatchFuncName != null:
        //            var isCompleted = (bool)InvokeCallback(GroupMatchFuncName, ToWaitsGroup());
        //            Status = isCompleted ? WaitStatus.Completed : Status;
        //            return isCompleted;

        //        case WaitType.GroupWaitWithExpression:
        //            completed = ChildWaits?.Any(x => x.Status == WaitStatus.Waiting) is false;
        //            break;
        //    }
        //    return completed;
        //}

        //internal override void OnAddWait()
        //{
        //    ActionOnChildrenTree(w => w.IsRoot = w.ParentWait == null && w.ParentWaitId == null);
        //    base.OnAddWait();
        //}
    
        //internal override bool ValidateWaitRequest()
        //{
        //    if (ChildWaits == null || !ChildWaits.Any())
        //    {
        //        WorkflowInstance.AddLog(
        //            $"The group wait named [{WaitName}] does not have childern, You must add one wait at least.",
        //            LogType.Error,
        //            StatusCodes.WaitValidation);
        //    }
        //    if (ChildWaits.Any(x => x == null))
        //    {
        //        WorkflowInstance.AddLog(
        //            $"The group wait named [{WaitName}] contains wait that has null value.",
        //            LogType.Error,
        //            StatusCodes.WaitValidation);
        //    }
        //    return base.ValidateWaitRequest();
        //}
    }
}