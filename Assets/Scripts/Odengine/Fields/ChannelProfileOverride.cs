using System;
using System.Collections.Generic;

namespace Odengine.Fields
{
    /// <summary>
    /// Optional per-channel overrides for field profiles (items, factions, etc.)
    /// Allows item-specific propagation behavior without creating separate fields.
    /// </summary>
    [Serializable]
    public sealed class ChannelProfileOverride
    {
        public float? PropagationRate;
        public float? EdgeResistanceScale;
        public float? DecayRate;
        public float? MinAmp;
        public Dictionary<string, float> TagMultipliers; // edge tag -> resistance multiplier

        public ChannelProfileOverride() { }
    }

    /// <summary>
    /// Provider interface for per-channel profile overrides.
    /// Implemented by systems that need item-specific physics (e.g., EconomySystem).
    /// </summary>
    public interface IChannelProfileProvider
    {
        /// <summary>
        /// Return null to use field default profile values.
        /// </summary>
        ChannelProfileOverride GetOverride(string channelId);
    }

    /// <summary>
    /// Helper to resolve effective parameters from base profile + optional override.
    /// </summary>
    public static class ProfileResolver
    {
        public static void Resolve(FieldProfile baseProfile, ChannelProfileOverride ov,
            out float propRate, out float resScale, out float decayRate, out float minAmp)
        {
            propRate = ov?.PropagationRate ?? baseProfile.PropagationRate;
            resScale = ov?.EdgeResistanceScale ?? baseProfile.EdgeResistanceScale;
            decayRate = ov?.DecayRate ?? baseProfile.DecayRate;
            minAmp = ov?.MinAmp ?? baseProfile.MinAmp;
        }

        public static float GetTagMultiplier(ChannelProfileOverride ov, string tag, float defaultValue = 1f)
        {
            if (ov?.TagMultipliers == null) return defaultValue;
            return ov.TagMultipliers.TryGetValue(tag, out var mult) ? mult : defaultValue;
        }
    }
}
