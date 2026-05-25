using System;

namespace InProcessSqliteSample
{
    public class ShipOrderCommand
    {
        public string OrderId { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
    }
}
