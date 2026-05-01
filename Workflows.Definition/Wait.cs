using System;

namespace Workflows.Definition
{
    /// <summary>
    /// Base class for all wait types in the workflow engine.
    /// Wraps a WaitInfrastructureDto (or derived) to provide runtime behavior and fluent configuration.
    /// </summary>
    public class Wait
    {
        public virtual DTOs.WaitInfrastructureDto ToDto() => WaitData;

        internal Wait(DTOs.WaitInfrastructureDto wait)
        {
            WaitData = wait;
        }

        /// <summary>
        /// The underlying infrastructure DTO containing persistence and execution state.
        /// </summary>
        internal DTOs.WaitInfrastructureDto WaitData { get; set; }

        /// <summary>
        /// Action to execute if this wait is cancelled.
        /// </summary>
        internal Action CancelAction { get; set; }

        /// <summary>
        /// Reference to the workflow container that created this wait.
        /// </summary>
        public Definition.WorkflowContainer CurrentWorkflow { get; set; }

        /// <summary>
        /// Validate delegate that used for groupMatchFilter, AfterMatchAction, CancelAction and return:
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