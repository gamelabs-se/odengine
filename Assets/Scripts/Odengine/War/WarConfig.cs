using System;

namespace Odengine.War
{
    /// <summary>
    /// Tuning constants for WarSystem dynamics.
    /// Passed to the WarSystem constructor and serialized in the system blob so that
    /// a resumed simulation uses exactly the same constants as the original run.
    ///
    /// All values are plain data — no Unity dependencies.
    /// </summary>
    [Serializable]
    public sealed class WarConfig
    {
        /// <summary>LogAmp added per tick at a node actively at war.</summary>
        public float ExposureGrowthRate { get; set; } = 0.05f;

        /// <summary>LogAmp removed per tick when a node is in ambient peace.</summary>
        public float AmbientDecayRate { get; set; } = 0.02f;

        /// <summary>LogAmp removed per tick during ceasefire cooldown (faster than ambient).</summary>
        public float CeasefireDecayRate { get; set; } = 0.06f;

        /// <summary>
        /// Treat exposure logAmp as effectively zero when below this value.
        /// Must be >= ScalarField.LogEpsilon to avoid fighting the field's pruning.
        /// </summary>
        public float ExposureEpsilon { get; set; } = 0.0001f;

        /// <summary>
        /// Internal channelId used for the single-channel exposure field.
        /// Opaque — has no semantic meaning outside WarSystem. Must not change
        /// after first use: field data is stored under this key in snapshots.
        /// </summary>
        public string ExposureChannelId { get; set; } = "x";

        /// <summary>Base occupation progress rate per tick for an unstable node.</summary>
        public float OccupationBaseRate { get; set; } = 0.1f;

        /// <summary>
        /// Scales how much node stability resists occupation progress.
        /// progress_rate = OccupationBaseRate / (1 + OccupationStabilityResist × stability)
        /// </summary>
        public float OccupationStabilityResist { get; set; } = 0.2f;
    }
}
