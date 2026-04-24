using System;
using System.Collections.Generic;
using System.Reflection;
namespace Workflows.Handler.InOuts.Entities
{
    public class WorkflowIdentifier : BasicMethodIdentifier
    {
        public string RF_MethodUrn { get; internal set; }
        public List<WaitEntity> WaitsCreatedByWorkflow { get; internal set; }
        public List<WorkflowInstance> ActiveWorkflowsStates { get; internal set; }
        public bool IsActive { get; internal set; } = true;


        public bool IsEntryPoint => Type == MethodType.WorkflowEntryPoint;

        private Type _classType;
        public Type InClassType =>
            _classType ??= Assembly.LoadFrom(AppContext.BaseDirectory + AssemblyName).GetType(ClassName);


        internal override void FillFromMethodData(MethodData methodData)
        {
            RF_MethodUrn = methodData.MethodUrn;
            IsActive = methodData.IsActive;
            base.FillFromMethodData(methodData);
        }
    }

}
