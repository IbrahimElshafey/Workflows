using System;
namespace Workflows.Handler.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [Injection(typeof(EmitSignalAspect), Inherited = true)]
    public class EmitSignalAttribute : Attribute, ITrackingIdentifier
    {
        public EmitSignalAttribute(string methodUrn)
        {
            MethodUrn = methodUrn;
        }

        public string MethodUrn { get; }
        public bool FromExternal { get; set; }
        public bool IsLocalOnly { get; set; }

        public override object TypeId => "1f220128-d0f7-4dac-ad81-ff942d68942c";

        public override string ToString()
        {
            return $"{MethodUrn},{FromExternal}";
        }
    }
}