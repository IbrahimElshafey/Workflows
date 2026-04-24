using Workflows.Handler.InOuts.Entities;
using System.Collections.Generic;
namespace Workflows.Handler.InOuts
{
    public interface IObjectWithLog
    {
        [IgnoreMember]
        [NotMapped]
        public List<LogRecord> Logs { get; set; }
    
        [IgnoreMember]
        [NotMapped]
        public EntityType EntityType { get;}
    }
}