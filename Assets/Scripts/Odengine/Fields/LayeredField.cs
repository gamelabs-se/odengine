using System;
using System.Collections.Generic;

namespace Odengine.Fields
{
    /// <summary>
    /// A field that supports multiple channels/layers (e.g., one layer per item/commodity).
    /// Stores Amplitude (float) at each (nodeId, channelId) pair.
    /// This prevents field explosion: instead of 1000 fields for 1000 items,
    /// you have 1 Availability field with 1000 channels.
    /// </summary>
    public sealed class OdLayeredField
    {
        public string FieldId { get; }
        public FieldProfile Profile { get; }
        
        // nodeId -> (channelId -> amplitude)
        private readonly Dictionary<string, Dictionary<string, float>> _amplitudes;

        public OdLayeredField(string fieldId, FieldProfile profile)
        {
            FieldId = fieldId ?? throw new ArgumentNullException(nameof(fieldId));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _amplitudes = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
        }

        public float GetAmplitude(string nodeId, string channelId = "default")
        {
            if (_amplitudes.TryGetValue(nodeId, out var channels) && 
                channels.TryGetValue(channelId, out var amp))
                return amp;
            return 0f;
        }

        public void SetAmplitude(string nodeId, string channelId, float amplitude)
        {
            if (!_amplitudes.TryGetValue(nodeId, out var channels))
            {
                channels = new Dictionary<string, float>(StringComparer.Ordinal);
                _amplitudes[nodeId] = channels;
            }
            channels[channelId] = amplitude;
        }

        public void ModifyAmplitude(string nodeId, string channelId, float delta)
        {
            var current = GetAmplitude(nodeId, channelId);
            SetAmplitude(nodeId, channelId, current + delta);
        }

        public IEnumerable<(string nodeId, string channelId, float amp)> GetAllAmplitudes()
        {
            foreach (var nodeKvp in _amplitudes)
            {
                foreach (var channelKvp in nodeKvp.Value)
                {
                    yield return (nodeKvp.Key, channelKvp.Key, channelKvp.Value);
                }
            }
        }

        /// <summary>
        /// Get all channels at a specific node.
        /// </summary>
        public IEnumerable<(string channelId, float amp)> GetChannelsAtNode(string nodeId)
        {
            if (_amplitudes.TryGetValue(nodeId, out var channels))
            {
                foreach (var kvp in channels)
                    yield return (kvp.Key, kvp.Value);
            }
        }

        public void Clear() => _amplitudes.Clear();

        public override string ToString() => $"LayeredField({FieldId}, nodes={_amplitudes.Count})";
    }
}
