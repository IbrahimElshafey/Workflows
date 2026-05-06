using System;

namespace Workflows.Abstraction.DTOs
{
    public class AsyncResult
    {
        public Guid Id { get; }

        public object Data { get; }

        public string Status { get; }

        public string Message { get; }

        public DateTime SentDate { get; }

        public AsyncResult(Guid Id, object data, string Status, string Message, DateTime CreatedAt)
        {
            this.Id = Id;
            this.Status = Status;
            this.Message = Message;
            this.SentDate = CreatedAt;
            Data = data;
        }
    }

    public enum AsyncStatus
    {
        Accepted = 100,
        Queued = 200,
        Processing = 300,
        Finished = 400,

        Rejected = -100,
        ProcessingFailed = -300,
        UnableToComplete = -400,
    }
}
