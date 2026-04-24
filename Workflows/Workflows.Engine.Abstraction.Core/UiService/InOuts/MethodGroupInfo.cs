using System;
using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.UiService.InOuts
{
    public class MethodGroupInfo
    {
        public MethodGroupInfo(
            MethodsGroup Group, int MethodsCount, int ActiveWaits, int CompletedWaits, int CanceledWaits, DateTime Created)
        {
            this.Group = Group;
            this.MethodsCount = MethodsCount;
            this.ActiveWaits = ActiveWaits;
            this.CompletedWaits = CompletedWaits;
            this.CanceledWaits = CanceledWaits;
            this.Created = Created;
        }

        public int AllWaitsCount => ActiveWaits + CompletedWaits + CanceledWaits;

        public MethodsGroup Group { get; }
        public int MethodsCount { get; }
        public int ActiveWaits { get; }
        public int CompletedWaits { get; }
        public int CanceledWaits { get; }
        public DateTime Created { get; }
    }
}
