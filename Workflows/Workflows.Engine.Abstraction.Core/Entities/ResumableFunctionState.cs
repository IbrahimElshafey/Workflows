using Workflows.Handler.InOuts.Entities.EntityBehaviour;
using System;
using System.Collections.Generic;
namespace Workflows.Handler.InOuts.Entities
{
    public class WorkflowInstance : IEntity<int>, IEntityWithUpdate, IEntityWithDelete, IBeforeSaveEntity, IObjectWithLog
    {
        public WorkflowInstance()
        {

        }
        [IgnoreMember]
        [NotMapped]
        public List<LogRecord> Logs { get; set; } = new();
        public int Id { get; internal set; }
        public int? ServiceId { get; internal set; }
        public DateTime Created { get; internal set; }
        /// <summary>
        /// Serialized class instance that contain the resumable workflow instance data
        /// </summary>
        [NotMapped]
        public object StateObject { get; internal set; }
        public byte[] StateObjectValue { get; internal set; }

        public List<WaitEntity> Waits { get; internal set; } = new();


        public WorkflowIdentifier WorkflowIdentifier { get; internal set; }
        public int WorkflowIdentifierId { get; internal set; }
        public WorkflowInstanceStatus Status { get; internal set; }
        public DateTime Modified { get; internal set; }
        public string ConcurrencyToken { get; internal set; }

        public bool IsDeleted { get; internal set; }

        public EntityType EntityType => EntityType.WorkflowInstanceLog;

        //public Closures Closures { get; internal set; } = new();

        public void BeforeSave()
        {
            var converter = new BinarySerializer();
            StateObjectValue = converter.ConvertToBinary(StateObject);
            //foreach (var wait in Waits)
            //{
            //    if (wait is MethodWait mw && mw.Closure != null)
            //    {
            //        Closures[mw.RequestedByWorkflowId] = mw.Closure;
            //    }
            //}
        }

        public void LoadUnmappedProps(Type stateObjectType)
        {
            var converter = new BinarySerializer();
            StateObject =
                stateObjectType != null ?
                    converter.ConvertToObject(StateObjectValue, stateObjectType) :
                    null;
        }
    }
}
