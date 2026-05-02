using Workflows.Definition;
using WorkflowSample.DataObject;

namespace WorkflowSample
{
    /// <summary>
    /// Example workflow demonstrating the Command primitive usage.
    /// </summary>
    public class OrderWithCommandWorkflow : WorkflowContainer
    {
        public int CurrentOrderId { get; set; }
        public string CustomerEmail { get; set; }
        public decimal OrderAmount { get; set; }

        public async IAsyncEnumerable<Wait> ProcessOrderWithCommand()
        {
            // Receive order details via signal
            yield return WaitSignal<OrderReceivedEvent>("OrderReceived")
                .MatchIf(x => x.OrderId > 0)
                .WithCancelToken("order-received-token")
                .AfterMatch(order =>
                {
                    CurrentOrderId = order.OrderId;
                    CustomerEmail = order.CustomerEmail;
                    OrderAmount = order.Amount;
                });

            // Command 1: Send confirmation email with retry and compensation
            yield return ExecuteCommand<SendEmailCommand, SendEmailResult>(
                "SendConfirmationEmail",
                new SendEmailCommand
                {
                    To = CustomerEmail,
                    Subject = "Order Confirmation",
                    Body = $"Your order {CurrentOrderId} has been received."
                })
                .WithRetries(maxAttempts: 3, backoff: TimeSpan.FromSeconds(5))
                .OnResult(result =>
                {
                    Console.WriteLine($"Confirmation email sent with MessageId: {result.MessageId}");
                })
                .RegisterCompensation(() =>
                {
                    Console.WriteLine("Compensating: Marking email delivery as failed in database");
                });

            // Command 2: Process payment with async compensation
            yield return ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                new ProcessPaymentCommand
                {
                    OrderId = CurrentOrderId.ToString(),
                    Amount = OrderAmount,
                    PaymentMethod = "CreditCard"
                })
                .WithRetries(maxAttempts: 2, backoff: TimeSpan.FromSeconds(10))
                .OnResult(async result =>
                {
                    Console.WriteLine($"Payment processed with TransactionId: {result.TransactionId}");
                    // Can perform async operations here - they will be awaited
                    await LogPaymentAsync(result);
                })
                .RegisterCompensation(async () =>
                {
                    Console.WriteLine("Compensating: Refunding payment");
                    await RefundPaymentAsync();
                });

            // Command 3: Send thank you email
            yield return ExecuteCommand<SendEmailCommand, SendEmailResult>(
                "SendThankYouEmail",
                new SendEmailCommand
                {
                    To = CustomerEmail,
                    Subject = "Thank You",
                    Body = $"Thank you for your order {CurrentOrderId}!"
                })
                .WithRetries(maxAttempts: 3);

            Console.WriteLine("Order processing workflow completed!");
        }

        private Task LogPaymentAsync(ProcessPaymentResult result)
        {
            return Task.Run(() =>
                Console.WriteLine($"Logged payment transaction: {result.TransactionId}")
            );
        }

        private Task RefundPaymentAsync()
        {
            return Task.Run(() =>
                Console.WriteLine("Initiated refund process")
            );
        }
    }
}
