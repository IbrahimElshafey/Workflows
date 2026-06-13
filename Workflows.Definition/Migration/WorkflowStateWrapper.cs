using System;
using System.Linq;
using System.Reflection;
using Workflows.Abstraction.DTOs;

namespace Workflows.Definition
{
    /// <summary>
    /// Typed wrapper over a suspended workflow instance's serialized state.
    /// Used exclusively in migration classes — not in live workflow execution.
    /// </summary>
    public abstract class WorkflowStateWrapper
    {
        public WorkflowStateDto Dto { get; }
        protected WorkflowStateWrapper(WorkflowStateDto dto) => Dto = dto;
    }

    public abstract class WorkflowStateWrapper<TInstance> : WorkflowStateWrapper
        where TInstance : class, new()
    {
        public TInstance Instance { get; }

        protected WorkflowStateWrapper(WorkflowStateDto dto, TInstance instance)
            : base(dto) => Instance = instance;

        /// <summary>
        /// Reflect-copies all public properties that match by name AND type
        /// from the source instance to this instance.
        /// Call this first, then fix up mismatches manually.
        /// </summary>
        public void AutoMapFrom<TOther>(WorkflowStateWrapper<TOther> source)
            where TOther : class, new()
        {
            var sourceProps = typeof(TOther)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var targetIndex = typeof(TInstance)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(p => p.Name, p => p);

            foreach (var sp in sourceProps)
            {
                if (!targetIndex.TryGetValue(sp.Name, out var tp)) continue;
                if (!tp.PropertyType.IsAssignableFrom(sp.PropertyType)) continue;
                if (!tp.CanWrite) continue;
                tp.SetValue(Instance, sp.GetValue(source.Instance));
            }
        }
    }
}
