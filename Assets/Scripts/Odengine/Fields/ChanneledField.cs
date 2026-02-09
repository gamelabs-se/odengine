using System;
using System.Collections.Generic;

namespace Odengine.Fields
{
    /// <summary>
    /// A field with multiple channels (layers).
    /// Example: One "Availability" field, channels = itemIds
    /// This prevents field explosion (1000 items → 1 field, not 1000 fields).
    /// </summary>
    public sealed class ChanneledField
    {
        public string FieldId { get; }
        public FieldProfile Profile { get; }
        
        // [nodeId][channelId] = amplitude
        private readonly Dictionary<string, Dictionary<string, float>> _amplitudes;

        public ChanneledField(string fieldId, FieldProfile profile)
        {
            FieldId = fieldId ?? throw new ArgumentNullException(nameof(fieldId));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _amplitudes = new Dictionary<string, Dictionary<string, float>>();
        }

        public float GetAmplitude(string nodeId, string channelId)
        {
            if (!_amplitudes.TryGetValue(nodeId, out var channels))
                return 0f;
            return channels.TryGetValue(channelId, out var amp) ? amp : 0f;
        }

        public void SetAmplitude(string nodeId, string channelId, float amplitude)
        {
            if (!_amplitudes.ContainsKey(nodeId))
                _amplitudes[nodeId] = new Dictionary<string, float>();
            
            _amplitudes[nodeId][channelId] = amplitude;
        }

        public void ModifyAmplitude(string nodeId, string channelId, float delta)
        {
            float current = GetAmplitude(nodeId, channelId);
            SetAmplitude(nodeId, channelId, current + delta);
        }

        /// <summary>
        /// Apply deltas for a specific channel (two-phase pattern).
        /// </summary>
        public void ApplyDeltas(string channelId, Dictionary<string, float> deltas)
        {
            foreach (var kvp in deltas)
            {
                ModifyAmplitude(kvp.Key, channelId, kvp.Value);
            }
        }

        public IEnumerable<string> GetChannels(string nodeId)
        {
            if (_amplitudes.TryGetValue(nodeId, out var channels))
                return channels.Keys;
            return Array.Empty<string>();
        }

        public void Clear() => _amplitudes.Clear();

        public override string ToString() => $"ChanneledField({FieldId}, nodes={_amplitudes.Count})";
    }
}
