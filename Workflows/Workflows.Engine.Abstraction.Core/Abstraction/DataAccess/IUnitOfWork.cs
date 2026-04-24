using System;using System.Threading.Tasks; namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface IUnitOfWork : IDisposable
    {
        Task<bool> CommitAsync();
        Task Rollback();
        void MarkEntityAsModified(object entity);
    }
}
