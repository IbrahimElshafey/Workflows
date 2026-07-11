using System;

namespace InProcessSqliteSample
{
    public class ShipOrderResult
    {
        public string OrderId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string TrackingNumber { get; set; } = string.Empty;
    }
}
