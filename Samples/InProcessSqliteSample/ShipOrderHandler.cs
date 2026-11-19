using System;
using System.Threading.Tasks;

namespace InProcessSqliteSample
{
    public class ShipOrderHandler
    {
        public Task<ShipOrderResult> HandleAsync(ShipOrderCommand command)
        {
            Console.Write("Shipment successful? (y/n): ");
            bool success = Console.ReadLine()?.Trim().ToLower() == "y";
            var result = new ShipOrderResult
            {
                Success = success,
                TrackingNumber = success ? $"TRK-{new Random().Next(100000, 999999)}" : string.Empty
            };
            return Task.FromResult(result);
        }
    }
}
