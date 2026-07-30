using System;

namespace Workflows.Definition
{
    /// <summary>
    /// Encapsulates a migrated Wait object alongside an optional explicit target StateIndex in Version 2.
    /// </summary>
    public class MigratedWait
    {
        public Wait Wait { get; }
        public int? TargetStateIndex { get; }

        public MigratedWait(Wait wait, int? targetStateIndex = null)
        {
            Wait = wait ?? throw new ArgumentNullException(nameof(wait));
            TargetStateIndex = targetStateIndex;
        }

        /// <summary>
        /// Implicit conversion from Wait to MigratedWait for seamless DX.
        /// </summary>
        public static implicit operator MigratedWait(Wait wait) => new MigratedWait(wait);
    }
}
