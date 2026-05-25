using System;

namespace InProcessSqliteSample
{
    public class ShipOrderResult
    {
        public bool Success { get; set; }
        public string TrackingNumber { get; set; } = string.Empty;
    }
}
