using System;
using System.Collections.Generic;

namespace Odengine.Fields
{
    public enum ConservationMode
    {
        /// <summary>Amplitude flows like a fluid - source reduces when transmitting</summary>
        Diffusion,
        /// <summary>Amplitude radiates - source unchanged, requires explicit decay</summary>
        Radiation
    }

    /// <summary>
    /// Defines how a field behaves:
    /// - How fast it propagates
    /// - How much it cares about edge resistance
    /// - Conservation mode (diffusion vs radiation)
    /// - Per-tag resistance multipliers
    /// </summary>
    public class FieldProfile
    {
        public string ProfileId { get; }
        public float PropagationRate { get; set; }
        public float EdgeResistanceScale { get; set; }
        public float DecayRate { get; set; }
        public float MinAmp { get; set; }
        public ConservationMode Mode { get; set; }
        
        private readonly Dictionary<string, float> _tagMultipliers = new Dictionary<string, float>();

        public FieldProfile(
            string profileId,
            float propagationRate = 1f,
            float edgeResistanceScale = 1f,
            float decayRate = 0f,
            float minAmp = 0.001f,
            ConservationMode mode = ConservationMode.Radiation)
        {
            ProfileId = profileId;
            PropagationRate = propagationRate;
            EdgeResistanceScale = edgeResistanceScale;
            DecayRate = decayRate;
            MinAmp = minAmp;
            Mode = mode;
        }

        public void SetTagMultiplier(string tag, float multiplier)
        {
            _tagMultipliers[tag] = multiplier;
        }

        public float GetTagMultiplier(IReadOnlyCollection<string> tags)
        {
            float result = 1f;
            foreach (var tag in tags)
            {
                if (_tagMultipliers.TryGetValue(tag, out float mult))
                    result *= mult;
            }
            return result;
        }

        public override string ToString() => $"Profile({ProfileId}, {Mode})";
    }
}
