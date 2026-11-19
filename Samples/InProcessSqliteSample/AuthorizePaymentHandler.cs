using System;
using System.Threading.Tasks;

namespace InProcessSqliteSample
{
    public class AuthorizePaymentHandler
    {
        public Task<PaymentResult> HandleAsync(PaymentRequest request)
        {
            Console.Write("Payment successful? (y/n): ");
            bool success = Console.ReadLine()?.Trim().ToLower() == "y";
            var result = new PaymentResult
            {
                Success = success,
                TransactionId = success ? $"TXN_{Guid.NewGuid().ToString()[..8].ToUpper()}" : string.Empty
            };
            return Task.FromResult(result);
        }
    }
}
