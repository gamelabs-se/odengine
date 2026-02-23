using System;
using System.Collections.Generic;
using System.Linq;

namespace Odengine.Fields
{
    /// <summary>
    /// Multi-channel scalar field with log-space storage.
    /// Neutral baseline: multiplier = 1.0 everywhere (logAmp = 0).
    /// Sparse: missing keys = neutral.
    /// </summary>
    [Serializable]
    public sealed class ScalarField
    {
        private struct FieldKey : IEquatable<FieldKey>
        {
            public string NodeId;
            public string ChannelId;

            public FieldKey(string nodeId, string channelId)
            {
                NodeId = nodeId;
                ChannelId = channelId;
            }

            public bool Equals(FieldKey other) =>
                NodeId == other.NodeId && ChannelId == other.ChannelId;

            public override bool Equals(object obj) =>
                obj is FieldKey key && Equals(key);

            public override int GetHashCode() =>
                (NodeId?.GetHashCode() ?? 0) * 397 ^ (ChannelId?.GetHashCode() ?? 0);
        }

        public string FieldId { get; }
        public FieldProfile Profile { get; }

        private readonly Dictionary<FieldKey, float> _logAmps;
        private const float LogEpsilon = 0.0001f;

        public ScalarField(string fieldId, FieldProfile profile)
        {
            if (string.IsNullOrEmpty(fieldId))
                throw new ArgumentException("FieldId cannot be null or empty", nameof(fieldId));
            
            FieldId = fieldId;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _logAmps = new Dictionary<FieldKey, float>();
        }

        public float GetLogAmp(string nodeId, string channelId)
        {
            if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(channelId))
                return 0f;
            
            var key = new FieldKey(nodeId, channelId);
            return _logAmps.TryGetValue(key, out float logAmp) ? logAmp : 0f;
        }

        public float GetMultiplier(string nodeId, string channelId)
        {
            float logAmp = GetLogAmp(nodeId, channelId);
            return MathF.Exp(logAmp);
        }

        public void SetLogAmp(string nodeId, string channelId, float logAmp)
        {
            if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(channelId))
                return;

            // Clamp
            logAmp = Math.Clamp(logAmp, Profile.MinLogAmpClamp, Profile.MaxLogAmpClamp);

            var key = new FieldKey(nodeId, channelId);

            // Remove if effectively zero (neutral)
            if (MathF.Abs(logAmp) < LogEpsilon)
            {
                _logAmps.Remove(key);
                return;
            }

            _logAmps[key] = logAmp;
        }

        public void AddLogAmp(string nodeId, string channelId, float deltaLogAmp)
        {
            if (MathF.Abs(deltaLogAmp) < LogEpsilon)
                return;

            float current = GetLogAmp(nodeId, channelId);
            SetLogAmp(nodeId, channelId, current + deltaLogAmp);
        }

        public ChannelView ForChannel(string channelId) => new ChannelView(this, channelId);

        public IReadOnlyList<string> GetActiveNodeIdsSortedForChannel(string channelId)
        {
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in _logAmps.Keys)
            {
                if (key.ChannelId == channelId)
                    nodeIds.Add(key.NodeId);
            }

            var list = nodeIds.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public IReadOnlyList<string> GetActiveChannelIdsSorted()
        {
            var channelIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in _logAmps.Keys)
                channelIds.Add(key.ChannelId);

            var list = channelIds.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>
        /// Returns all active channelIds at a specific node in Ordinal-sorted order.
        /// Only channels with abs(logAmp) > LogEpsilon are included (same sparsity rule as storage).
        /// Returns an empty list when the node has no active channels.
        /// </summary>
        public IReadOnlyList<string> GetActiveChannelIdsSortedForNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return Array.Empty<string>();

            var channelIds = new List<string>();
            foreach (var key in _logAmps.Keys)
            {
                if (key.NodeId == nodeId)
                    channelIds.Add(key.ChannelId);
            }

            channelIds.Sort(StringComparer.Ordinal);
            return channelIds;
        }

        /// <summary>
        /// Returns all nodeIds that have at least one active channel, in Ordinal-sorted order.
        /// </summary>
        public IReadOnlyList<string> GetActiveNodeIdsSorted()
        {
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in _logAmps.Keys)
                nodeIds.Add(key.NodeId);

            var list = new List<string>(nodeIds);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>
        /// Returns the channelId with the highest logAmp at <paramref name="nodeId"/>,
        /// or <c>null</c> if no channel at this node has a positive logAmp.
        ///
        /// Neutral baseline is 0 (absent entries). A channel must beat 0 to be considered
        /// dominant — negative logAmps are below the neutral baseline and are never returned.
        ///
        /// Tie-breaking: when two channels share the exact same logAmp, the Ordinal-first
        /// channelId is returned, keeping the result deterministic.
        ///
        /// Typical use: argmax over faction presence channels to derive territorial control.
        /// </summary>
        public string GetDominantChannel(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;

            string dominant = null;
            float bestLogAmp = 0f; // must exceed neutral baseline to qualify

            foreach (var kvp in _logAmps)
            {
                if (kvp.Key.NodeId != nodeId) continue;

                float logAmp = kvp.Value;
                if (logAmp <= 0f) continue; // below or at neutral — not dominant

                bool beats = logAmp > bestLogAmp
                          || (logAmp == bestLogAmp
                              && StringComparer.Ordinal.Compare(kvp.Key.ChannelId, dominant) < 0);

                if (beats)
                {
                    bestLogAmp = logAmp;
                    dominant = kvp.Key.ChannelId;
                }
            }

            return dominant;
        }

        public IEnumerable<(string nodeId, string channelId, float logAmp)> EnumerateAllActiveSorted()
        {
            var sorted = _logAmps
                .Select(kvp => (kvp.Key.NodeId, kvp.Key.ChannelId, kvp.Value))
                .OrderBy(x => x.ChannelId, StringComparer.Ordinal)
                .ThenBy(x => x.NodeId, StringComparer.Ordinal);

            return sorted;
        }
    }
}
