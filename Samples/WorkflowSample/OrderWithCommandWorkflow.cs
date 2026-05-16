using Workflows.Definition;
using WorkflowSample.DataObject;

namespace WorkflowSample
{
    /// <summary>
    /// Example workflow demonstrating the Command primitive usage.
    /// </summary>
    public sealed class OrderWithCommandWorkflow : WorkflowContainer
    {
        public int CurrentOrderId { get; set; }
        public string CustomerEmail { get; set; }
        public decimal OrderAmount { get; set; }

        public override async IAsyncEnumerable<Wait> Run()
        {
            // Receive order details via signal
            yield return WaitSignal<OrderReceivedEvent>("OrderReceived")
                .WithState(0)
                .MatchIf((x, minOrderId) => x.OrderId > minOrderId)
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
                .WithState(CurrentOrderId)
                .WithRetries(maxAttempts: 3, backoff: TimeSpan.FromSeconds(5))
                .OnResult((result, orderId) =>
                {
                    Console.WriteLine($"Confirmation email sent with MessageId: {result.MessageId} for order {orderId}");
                })
                .RegisterCompensation(result =>
                {
                    Console.WriteLine($"Compensating: Marking email delivery as failed in database for order {CurrentOrderId}");
                    return default;
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
                .WithState(CustomerEmail)
                .WithRetries(maxAttempts: 2, backoff: TimeSpan.FromSeconds(10))
                .OnResult((result, email) =>
                {
                    Console.WriteLine($"Payment processed with TransactionId: {result.TransactionId} for {email}");
                    return;
                })
                .RegisterCompensation(async (result, email) =>
                {
                    Console.WriteLine($"Compensating: Refunding payment for {email}");
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
