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

        public FieldProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                throw new ArgumentException("ProfileId cannot be null or empty", nameof(profileId));
            ProfileId = profileId;
        }
    }
}
