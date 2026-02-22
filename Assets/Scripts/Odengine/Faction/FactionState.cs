using System;

namespace Odengine.Faction
{
    /// <summary>
    /// Per-faction derived state, recomputed every Tick by FactionSystem.
    /// All values are read-only from outside the Faction namespace.
    /// </summary>
    [Serializable]
    public sealed class FactionState
    {
        public string FactionId { get; }

        /// <summary>
        /// Long-term political health [0..1].
        /// Exponentially smooths toward AverageStability each tick.
        /// Starts at 1 (fully stable).
        /// </summary>
        public float PoliticalStability { get; internal set; } = 1f;

        /// <summary>
        /// Accumulated war weariness [0..1].
        /// Grows when AverageWarExposure is above threshold, decays in peace.
        /// </summary>
        public float WarExhaustion { get; internal set; } = 0f;

        /// <summary>Number of nodes currently controlled by this faction.</summary>
        public int ControlledNodeCount { get; internal set; }

        /// <summary>
        /// Mean node stability [0..1] across all controlled nodes this tick.
        /// When no nodes are controlled, mirrors PoliticalStability (no pressure either way).
        /// </summary>
        public float AverageStability { get; internal set; } = 1f;

        /// <summary>
        /// Mean war exposure (logAmp) across all controlled nodes this tick.
        /// 0 = no war. Sourced from WarSystem.GetExposureLogAmp via game-layer bridge.
        /// </summary>
        public float AverageWarExposure { get; internal set; }

        /// <summary>True when PoliticalStability is below FactionSystem.StabilityCrisisThreshold.</summary>
        public bool IsCollapsing { get; internal set; }

        public FactionState(string factionId)
        {
            if (string.IsNullOrEmpty(factionId))
                throw new ArgumentException("FactionId cannot be null or empty", nameof(factionId));
            FactionId = factionId;
        }
    }
}
