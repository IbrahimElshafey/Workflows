using Workflows.Handler.InOuts.Entities.EntityBehaviour;
using Workflows.Handler.Abstraction.Serialization;
using System;
using System.Collections.Generic;
namespace Workflows.Handler.InOuts.Entities
{
    public class WorkflowInstance : IEntity<int>, IEntityWithUpdate, IEntityWithDelete, IBeforeSaveEntity, IObjectWithLog
    {
        public WorkflowInstance()
        {

        }
        public List<LogRecord> Logs { get; set; } = new List<LogRecord>();
        public int Id { get; internal set; }
        public int? ServiceId { get; internal set; }
        public DateTime Created { get; internal set; }
        /// <summary>
        /// Serialized class instance that contain the resumable workflow instance data
        /// </summary>
        public object StateObject { get; internal set; }
        public byte[] StateObjectValue { get; internal set; }

        public List<WaitEntity> Waits { get; internal set; } = new List<WaitEntity>();


        public WorkflowIdentifier WorkflowIdentifier { get; internal set; }
        public int WorkflowIdentifierId { get; internal set; }
        public WorkflowInstanceStatus Status { get; internal set; }
        public DateTime Modified { get; internal set; }
        public string ConcurrencyToken { get; internal set; }

        public bool IsDeleted { get; internal set; }

        public EntityType EntityType => EntityType.WorkflowInstanceLog;

        //public Closures Closures { get; internal set; } = new();

        private static IBinarySerializer _binarySerializer;

        /// <summary>
        /// Sets the binary serializer implementation to use
        /// </summary>
        public static void SetBinarySerializer(IBinarySerializer binarySerializer)
        {
            _binarySerializer = binarySerializer ?? throw new ArgumentNullException(nameof(binarySerializer));
        }

        public void BeforeSave()
        {
            if (_binarySerializer == null)
                throw new InvalidOperationException("Binary serializer not configured. Call SetBinarySerializer first.");

            StateObjectValue = _binarySerializer.Serialize(StateObject);
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
            if (_binarySerializer == null)
                throw new InvalidOperationException("Binary serializer not configured. Call SetBinarySerializer first.");

            StateObject =
                stateObjectType != null ?
                    _binarySerializer.Deserialize(StateObjectValue, stateObjectType) :
                    null;
        }
    }
}
