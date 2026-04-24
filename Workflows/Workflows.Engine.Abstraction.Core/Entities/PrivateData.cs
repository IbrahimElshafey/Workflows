using System;
using System.Collections.Generic;
using Workflows.Handler.InOuts.Entities.EntityBehaviour;
namespace Workflows.Handler.InOuts.Entities
{
    public class PrivateData : IEntity<long>, IEntityWithUpdate, IAfterChangesSaved, IBeforeSaveEntity
    {
        public long Id { get; internal set; }
        public object Value { get; internal set; }
        public string TypeName { get; internal set; }
        public List<WaitEntity> ClosureLinkedWaits { get; internal set; }
        public List<WaitEntity> LocalsLinkedWaits { get; internal set; }

        public DateTime Created { get; internal set; }

        public int? ServiceId { get; internal set; }

        public DateTime Modified { get; internal set; }

        public string ConcurrencyToken { get; internal set; }
        public int? WorkflowInstanceId { get; internal set; }

        public void AfterChangesSaved()
        {
            SetWorkflowInstanceId();
        }

        public void BeforeSave()
        {
            SetWorkflowInstanceId();
            TypeName = Value?.GetType().Name;
        }

        private void SetWorkflowInstanceId()
        {
            if (WorkflowInstanceId != null && WorkflowInstanceId > 0) return;
            var stateId =
               LocalsLinkedWaits?.FirstOrDefault()?.WorkflowInstanceId ??
               ClosureLinkedWaits?.FirstOrDefault()?.WorkflowInstanceId;
            if (stateId == null || stateId == 0)
            {
                stateId =
                LocalsLinkedWaits?.FirstOrDefault()?.WorkflowInstance?.Id ??
                ClosureLinkedWaits?.FirstOrDefault()?.WorkflowInstance?.Id;
            }
            WorkflowInstanceId = stateId;
        }

        public T GetProp<T>(string propName)
        {
            switch (Value)
            {
                case JObject jobject:
                    return jobject[propName].ToObject<T>();
                case object closureObject:
                    return (T)closureObject.GetType().GetField(propName).GetValue(closureObject);
                default: return default;
            }
        }

        internal object AsType(Type closureClass)
        {
            Value = Value is JObject jobject ? jobject.ToObject(closureClass) : Value;
            return Value ?? Activator.CreateInstance(closureClass);
        }
    }
}
