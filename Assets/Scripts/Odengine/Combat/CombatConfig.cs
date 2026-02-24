using System;

namespace Odengine.Combat
{
    /// <summary>
    /// Tuning constants for CombatSystem dynamics.
    ///
    /// Passed to the CombatSystem constructor. All values are plain data — no Unity dependencies.
    ///
    /// Combat model overview:
    ///   Each node tracks per-faction combat intensity (logAmp).
    ///   Every tick, each faction is attrited by the combined strength of all opposing factions
    ///   at the same node.  The FieldProfile's DecayRate governs natural peacetime dissipation.
    /// </summary>
    [Serializable]
    public sealed class CombatConfig
    {
        /// <summary>
        /// Fraction of opposing total logAmp removed from each faction per unit time.
        /// Attrition delta = sum(opponent logAmps) × AttritionRate × dt (in log-space).
        /// Higher values → faster mutual destruction.
        /// </summary>
        public float AttritionRate { get; set; } = 0.3f;

        /// <summary>
        /// Minimum logAmp magnitude to consider a faction active at a node.
        /// Must be >= the intensity field's LogEpsilon to avoid fighting the field's pruning.
        /// </summary>
        public float ActiveThreshold { get; set; } = 0.0001f;
    }
}
