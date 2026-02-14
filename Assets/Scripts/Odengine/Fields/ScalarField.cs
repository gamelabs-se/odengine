using System;
using System.Collections.Generic;

namespace Odengine.Fields
{
    /// <summary>
    /// A scalar field over the graph with many channels (item IDs, faction IDs, etc.).
    /// This is the SINGLE field instance that backs many "virtual fields".
    /// 
    /// Key concept: 
    /// - Field has base amplitude per node (the "general field strength")
    /// - Channels are "realized layers" when they deviate from field amplitude
    /// - When channel amp gets close to field amp, it merges back (lazy virtualization)
    /// </summary>
    public sealed class ScalarField : Field
    {
        public override string FieldId { get; }
        public FieldProfile Profile { get; }
        public ChannelFieldStorage Storage { get; } = new ChannelFieldStorage();

        /// <summary>
        /// Base field amplitude per node (when channel not explicitly tracked)
        /// </summary>
        private readonly Dictionary<string, float> _fieldAmplitude = new(StringComparer.Ordinal);

        public IChannelProfileProvider ChannelProfileProvider { get; set; }

        /// <summary>
        /// Delta threshold: if |channelAmp - fieldAmp| < this, merge channel into field
        /// </summary>
        public float MergeThreshold { get; set; } = 0.1f;

        /// <summary>
        /// Normalization threshold: only normalize channels if deviation is below this
        /// Large spikes have game implications and shouldn't be smoothed away
        /// </summary>
        public float NormalizationThreshold { get; set; } = 2.0f;

        /// <summary>
        /// Normalization rate per tick (0-1, how strongly to pull channel toward field)
        /// </summary>
        public float NormalizationRate { get; set; } = 0.1f;

        public ScalarField(string fieldId, FieldProfile profile)
        {
            FieldId = fieldId ?? throw new ArgumentNullException(nameof(fieldId));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        /// <summary>
        /// Get base field amplitude at node (fallback when channel not tracked)
        /// </summary>
        public float GetFieldAmp(string nodeId)
        {
            return _fieldAmplitude.TryGetValue(nodeId, out var amp) ? amp : 0f;
        }

        /// <summary>
        /// Set base field amplitude at node
        /// </summary>
        public void SetFieldAmp(string nodeId, float amp)
        {
            if (Math.Abs(amp) < 0.0001f)
                _fieldAmplitude.Remove(nodeId);
            else
                _fieldAmplitude[nodeId] = amp;
        }

        /// <summary>
        /// Set base amplitude (alias for tests)
        /// </summary>
        public void SetBaseAmplitude(string nodeId, float amp) => SetFieldAmp(nodeId, amp);

        /// <summary>
        /// Get a "virtual field" view for a specific channel.
        /// This is the "each item is a field" facade that keeps the mental model clean.
        /// </summary>
        public VirtualField For(string channelId) => new VirtualField(this, channelId);

        /// <summary>
        /// Process merge/split logic after propagation.
        /// Called once per tick to maintain lazy virtualization.
        /// </summary>
        public void ProcessVirtualization()
        {
            var channelsToRemove = new List<string>();

            foreach (var channelId in Storage.GetActiveChannelsSorted())
            {
                var channelMap = Storage.GetChannelMap(channelId);
                var nodesToMerge = new List<string>();

                foreach (var nodeId in channelMap.Keys)
                {
                    float channelAmp = channelMap[nodeId];
                    float fieldAmp = GetFieldAmp(nodeId);
                    float delta = Math.Abs(channelAmp - fieldAmp);

                    // Merge: channel is close enough to field, stop tracking it
                    if (delta < MergeThreshold)
                    {
                        nodesToMerge.Add(nodeId);
                        continue;
                    }

                    // Normalize: pull channel toward field if deviation is small
                    if (delta < NormalizationThreshold)
                    {
                        float normalized = channelAmp + (fieldAmp - channelAmp) * NormalizationRate;
                        Storage.Set(channelId, nodeId, normalized);
                    }
                }

                // Remove merged nodes
                foreach (var nodeId in nodesToMerge)
                {
                    channelMap.Remove(nodeId);
                }

                // If channel has no nodes left, remove it entirely
                if (channelMap.Count == 0)
                {
                    channelsToRemove.Add(channelId);
                }
            }

            // Clean up empty channels
            foreach (var channelId in channelsToRemove)
            {
                Storage.GetChannelMap(channelId).Clear();
            }
        }

        /// <summary>
        /// Get sorted node IDs that have field amplitude
        /// </summary>
        public List<string> GetFieldNodesSorted()
        {
            var list = new List<string>(_fieldAmplitude.Keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }

    /// <summary>
    /// VirtualField is the "each item is a field" facade.
    /// It's a lightweight struct that wraps (ScalarField, channelId).
    /// Conceptually it represents "water availability field", but physically it's just a view.
    /// 
    /// Key behavior:
    /// - GetAmp: returns channel amp if tracked, otherwise field amp (fallback)
    /// - SetAmp/AddAmp: if deviation exceeds threshold, realizes the channel (starts tracking)
    /// </summary>
    public readonly struct VirtualField
    {
        public readonly ScalarField Field;
        public readonly string ChannelId;

        public VirtualField(ScalarField field, string channelId)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            ChannelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
        }

        /// <summary>
        /// Get amplitude: returns channel-specific if tracked, otherwise field amplitude
        /// </summary>
        public float GetAmp(string nodeId)
        {
            // If channel is explicitly tracked at this node, use it
            if (Field.Storage.HasChannel(ChannelId))
            {
                var channelMap = Field.Storage.GetChannelMap(ChannelId);
                if (channelMap.ContainsKey(nodeId))
                {
                    return channelMap[nodeId];
                }
            }

            // Otherwise fallback to field amplitude
            return Field.GetFieldAmp(nodeId);
        }

        /// <summary>
        /// Set amplitude: realizes channel if deviation from field amp exceeds threshold
        /// </summary>
        public void SetAmp(string nodeId, float value)
        {
            float fieldAmp = Field.GetFieldAmp(nodeId);
            float delta = Math.Abs(value - fieldAmp);

            // If setting to near-field value, don't track
            if (delta < Field.MergeThreshold)
            {
                // Remove from channel if it exists
                if (Field.Storage.HasChannel(ChannelId))
                {
                    Field.Storage.GetChannelMap(ChannelId).Remove(nodeId);
                }
                return;
            }

            // Deviation is significant, track this channel
            Field.Storage.TouchChannel(ChannelId);
            Field.Storage.Set(ChannelId, nodeId, value);
        }

        /// <summary>
        /// Add delta to amplitude: realizes channel if result deviates from field
        /// </summary>
        public void AddAmp(string nodeId, float delta)
        {
            if (Math.Abs(delta) < 0.0001f) return;

            float current = GetAmp(nodeId); // Uses field amp if not tracked
            float newValue = current + delta;

            SetAmp(nodeId, newValue); // SetAmp handles merge/split logic
        }

        public override string ToString() => $"VirtualField({Field.FieldId}.{ChannelId})";
    }
}
