using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
namespace Workflows.Handler.BaseUse
{
    public class Wait
    {
        public virtual WaitBaseDto ToDto() => WaitData;
        internal Wait(WaitBaseDto wait)
        {
            WaitData = wait;
        }

        internal WaitBaseDto WaitData { get; set; }
        internal Action CancelAction { get; set; }
        public WorkflowContainer CurrentWorkflow { get; set; }

        /// <summary>
        /// Validate delegate that used for groupMatchFilter,AfterMatchAction,CancelAction and return:
        /// $"{method.DeclaringType.FullName}#{method.Name}"
        /// </summary>
        internal string ValidateCallback(Delegate callback, string methodName)
        {
            //var Name = WaitData.WaitName;
            //var method = callback.Method;
            //var workflowClassType = CurrentWorkflow.GetType();
            //var declaringType = method.DeclaringType;
            //var containerType = callback.Target?.GetType();

            //var validConatinerCalss =
            //  (declaringType == workflowClassType ||
            //  declaringType.Name == Constants.CompilerStaticLambdas ||
            //  declaringType.Name.StartsWith(Constants.CompilerClosurePrefix)) &&
            //  declaringType.FullName.StartsWith(workflowClassType.FullName);

            //if (validConatinerCalss is false)
            //    throw new Exception(
            //        $"For wait [{Name}] the [{methodName}:{method.Name}] must be a method in class " +
            //        $"[{workflowClassType.Name}] or inline lambda method.");

            //var hasOverload = workflowClassType.GetMethods(CoreExtensions.DeclaredWithinTypeFlags()).Count(x => x.Name == method.Name) > 1;
            //if (hasOverload)
            //    throw new Exception(
            //        $"For wait [{Name}] the [{methodName}:{method.Name}] must not be over-loaded.");
            //if (declaringType.Name.StartsWith(Constants.CompilerClosurePrefix))
            //    SetClosureObject(callback.Target);
            //return $"{method.DeclaringType.FullName}#{method.Name}";
            return null;
        }

        internal void SetClosureObject(object closure)
        {
            //var ClosureObject = this.WaitData.ClosureObject;
            //if (closure == null) return;
            //if (ClosureObject == null)
            //{
            //    ClosureObject = closure;
            //    return;
            //}

            //var currentClosureType = ClosureObject.GetType();
            //var incomingClosureType = closure.GetType();
            //var nestedClosure =
            //    currentClosureType.Name.StartsWith(Constants.CompilerClosurePrefix) is true &&
            //    incomingClosureType.Name.StartsWith(Constants.CompilerClosurePrefix);
            //var sameType = currentClosureType == incomingClosureType;
            //if (sameType is false && nestedClosure is false)
            //{
            //    throw new Exception(
            //        $"For method wait [{WaitData.WaitName}] the closure must be the same for AfterMatchAction, CancelAction, and MatchExpression.");
            //}
            //ClosureObject = closure;
        }
    }
}