using System;
namespace Workflows.Handler.Attributes
{
    /// <summary>
    ///     Start point for a resumable workflow
    /// </summary>
    /// 
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]

    public sealed class WorkflowAttribute : Attribute, ITrackingIdentifier
    {
        public override object TypeId => "6d68b97e-b8fe-4550-ad7e-5056022ff81a";
        public string MethodUrn { get; }
        public bool IsActive { get; }

        public WorkflowAttribute(string methodUrn, bool isActive = true)
        {
            MethodUrn = methodUrn;
            IsActive = isActive;
        }
    }
}