using System;

namespace InProcessSqliteSample
{
    public class ShipOrderCommand : Workflows.Abstraction.Runner.IDeferredCommand<ShipOrderCommand, ShipOrderResult>
    {
        public string OrderId { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;

        System.Linq.Expressions.Expression<Func<ShipOrderCommand, ShipOrderResult, bool>> Workflows.Abstraction.Runner.IDeferredCommand<ShipOrderCommand, ShipOrderResult>.MatchingFunction =>
            (input, result) => result.OrderId == input.OrderId;
    }
}
