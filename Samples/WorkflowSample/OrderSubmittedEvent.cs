using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Handler;
using Workflows.Handler.BaseUse;

namespace Workflows.Sample
{
    // --- Sample Signal Data Payloads ---
    public class OrderSubmittedEvent
    {
        public int OrderId { get; set; }
        public string CustomerName { get; set; }
    }
}