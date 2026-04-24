using FakeOrderManagmentRequest.Workflow;
using Workflows.Handler.Attributes;

namespace FakeOrderManagmentRequest.Services
{
    public class OrderProcessingService : IOrderProcessingService
    {
        [EmitSignal("OrderCreated")]
        public OrderCreationResult OrderCreated(Order order)
        {
            return new OrderCreationResult { OrderId = Random.Shared.Next() };
        }

        public async Task<int> ReviewOrderTask(int orderId)
        {
            return Random.Shared.Next();
        }

        [EmitSignal("FinanceTeamReviewTask")]
        public OrderReviewTaskResult FinanceTeamReviewTask(OrderReviewTask reviewTask)
        {
            //throw new Exception("Something wrong happened.");
            return new OrderReviewTaskResult();
        }
        public void SendNotification(int clientId, string msg)
        {
            Console.WriteLine($"Notify client {clientId} [{msg}]");
        }

        public int CreateRefundTask(int orderId)
        {
            return Random.Shared.Next();
        }

        [EmitSignal("FinanceTeamRefundOrder")]
        public RefundTaskResult FinanceTeamRefundOrder(RefundTask refundTask)
        {
            return new();
        }
    }
}
