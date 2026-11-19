using System;
using System.Threading.Tasks;
using Workflows.Storage.EntityFrameworkCore;

namespace Workflows.Hosting.InProcess
{
    public class CommandResultInboxWriter
    {
        private readonly WorkflowsDbContext _dbContext;

        public CommandResultInboxWriter(WorkflowsDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task WriteAsync(string commandWaitId, object result, bool isSuccess)
        {
            var entity = new CommandResultEntity
            {
                GlobalId = Guid.NewGuid(),
                ReceivedAt = DateTime.UtcNow,
                Status = 0, // Pending
                CommandWaitId = commandWaitId,
                ResultJson = result != null ? Newtonsoft.Json.JsonConvert.SerializeObject(result) : string.Empty,
                IsSuccess = isSuccess
            };
            _dbContext.CommandResults.Add(entity);
            await _dbContext.SaveChangesAsync();
        }
    }
}
