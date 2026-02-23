using System;

namespace Odengine.Fields
{
    [Serializable]
    public sealed class FieldProfile
    {
        public string ProfileId { get; }
        public float PropagationRate { get; set; } = 1f;
        public float EdgeResistanceScale { get; set; } = 1f;
        public float DecayRate { get; set; } = 0f;
        public float MinLogAmpClamp { get; set; } = -20f;
        public float MaxLogAmpClamp { get; set; } = 20f;

        /// <summary>
        /// Sparsity threshold. Entries with |logAmp| below this value are pruned from
        /// storage and treated as neutral (logAmp = 0, multiplier = 1).
        /// Stored in every snapshot so that a loaded simulation runs with exactly the
        /// same precision as the run that produced it.
        /// </summary>
        public float LogEpsilon { get; set; } = 0.0001f;

        public FieldProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                throw new ArgumentException("ProfileId cannot be null or empty", nameof(profileId));
            ProfileId = profileId;
        }
    }
}
