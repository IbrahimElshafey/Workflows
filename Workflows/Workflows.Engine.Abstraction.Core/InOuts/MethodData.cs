using Workflows.Handler.Attributes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using System;
using Workflows.Handler.Helpers;
using System.Linq;
namespace Workflows.Handler.InOuts
{
    public class MethodData
    {
        private MethodInfo _methodInfo;

        public MethodData(
            string assemblyName,
            string className,
            string methodName,
            string methodSignature,
            byte[] methodHash,
            string methodUrn,
            bool canPublishFromExternal,
            bool isLocalOnly,
            bool isActive)
        {
            AssemblyName = assemblyName;
            ClassName = className;
            MethodName = methodName;
            MethodSignature = methodSignature;
            MethodHash = methodHash;
            MethodUrn = methodUrn;
            CanPublishFromExternal = canPublishFromExternal;
            IsLocalOnly = isLocalOnly;
            IsActive = isActive;
        }
        public MethodData()
        {

        }

        public MethodData(MethodInfo methodInfo)
        {
            if (methodInfo == null) return;
            AssemblyName = methodInfo.DeclaringType?.Assembly.GetName().Name;
            ClassName = methodInfo.DeclaringType?.FullName;
            MethodName = methodInfo.Name;
            MethodSignature = CalcSignature(methodInfo);
            MethodUrn = GetMethodUrn(methodInfo);
            MethodHash = GetMethodHash();
            SetGroupProps(methodInfo);
        }

        private void SetGroupProps(MethodInfo methodInfo)
        {
            var pushCallAttribute = methodInfo
              .GetCustomAttributes()
              .FirstOrDefault(attribute => attribute.TypeId == (object)AttributeTypeIds.EmitSignal);

            if (pushCallAttribute == null) return;

            //CanPublishFromExternal = pushCallAttribute.FromExternal;
            //IsLocalOnly = pushCallAttribute.IsLocalOnly;
        }

        public string MethodUrn { get; set; }
        public string AssemblyName { get; internal set; }
        public string ClassName { get; internal set; }
        public string MethodName { get; internal set; }
        public string MethodSignature { get; internal set; }
        public byte[] MethodHash { get; internal set; }
        private string GetMethodUrn(MethodInfo method)
        {
            var trackId = method
                .GetCustomAttributes()
                .FirstOrDefault(
                attribute =>
                    attribute is WorkflowAttribute ||
                    attribute is SubWorkflowAttribute ||
                    attribute.TypeId == (object)AttributeTypeIds.EmitSignal
                );

            return (trackId as ITrackingIdentifier)?.MethodUrn ?? MethodInfo.Name;
        }

        internal MethodInfo MethodInfo
        {
            get
            {
                if (_methodInfo == null)
                    _methodInfo = CoreExtensions.GetMethodInfo(AssemblyName, ClassName, MethodName, MethodSignature);
                return _methodInfo;
            }
        }

        public MethodType MethodType { get; internal set; }
        public bool CanPublishFromExternal { get; internal set; }
        public bool IsLocalOnly { get; internal set; }
        public bool IsActive { get; internal set; } = true;

        internal static string CalcSignature(MethodBase value)
        {
            var parameterInfos = value.GetParameters();
            var inputs = parameterInfos.Length != 0
                ? parameterInfos
                    .Select(x => x.ParameterType.GetRealTypeName())
                    .Aggregate((x, y) => $"{x}#{y}")
                : string.Empty;
            if (value is MethodInfo methodInfo)
                return $"{methodInfo.ReturnType.GetRealTypeName()}#{inputs}";
            return inputs;
        }

        private byte[] GetMethodHash()
        {
            var input = string.Concat(MethodUrn, ClassName, AssemblyName, MethodSignature);
            using var md5 = MD5.Create();
            var inputBytes = Encoding.ASCII.GetBytes(input);
            return md5.ComputeHash(inputBytes);
        }

        public override string ToString()
        {
            return $"[{AssemblyName} # {ClassName}.{MethodName} # {MethodSignature}] with type {MethodType}";
        }

        internal bool Validate()
        {
            if (string.IsNullOrWhiteSpace(MethodUrn))
                throw new NullReferenceException($"For method ({this}) MethodUrn must not be null");
            return true;
        }
    }


}
