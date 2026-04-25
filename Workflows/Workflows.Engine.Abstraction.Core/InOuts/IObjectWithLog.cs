using Workflows.Handler.InOuts.Entities;
using System.Collections.Generic;
namespace Workflows.Handler.InOuts
{
    public interface IObjectWithLog
    {
        public List<LogRecord> Logs { get; set; }

        public EntityType EntityType { get;}
    }
}