using System;

namespace InProcessSqliteSample
{
    public class StockConfirmedSignal
    {
        public string OrderId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // e.g. "Available", "OutOfStock"
    }
}
