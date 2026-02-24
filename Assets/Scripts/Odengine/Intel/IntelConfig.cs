using System;

namespace Odengine.Intel
{
    /// <summary>
    /// Tuning constants for IntelSystem.
    /// All values are plain data — no Unity dependencies.
    /// </summary>
    [Serializable]
    public sealed class IntelConfig
    {
        /// <summary>
        /// Minimum logAmp to consider a faction's coverage "active" at a node.
        /// Must be >= the coverage field's LogEpsilon to avoid fighting field pruning.
        /// </summary>
        public float ActiveCoverageThreshold { get; set; } = 0.0001f;
    }
}
