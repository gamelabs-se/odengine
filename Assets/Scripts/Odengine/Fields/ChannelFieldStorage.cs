using System;
using System.Collections.Generic;

namespace Odengine.Fields
{
    /// <summary>
    /// Stores amplitude[channelId][nodeId] = float
    /// Determinism: channel IDs and node IDs are always iterated sorted.
    /// This is the runtime storage that backs "virtual fields per item".
    /// </summary>
    public sealed class ChannelFieldStorage
    {
        private readonly Dictionary<string, Dictionary<string, float>> _ampByChannel
            = new(StringComparer.Ordinal);

        private readonly HashSet<string> _activeChannels = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> ActiveChannels => _activeChannels;

        public bool HasChannel(string channelId) => _ampByChannel.ContainsKey(channelId);

        public bool HasActiveChannel(string channelId) => _activeChannels.Contains(channelId);

        public void TouchChannel(string channelId)
        {
            _activeChannels.Add(channelId);
            if (!_ampByChannel.ContainsKey(channelId))
                _ampByChannel[channelId] = new Dictionary<string, float>(StringComparer.Ordinal);
        }

        public float Get(string channelId, string nodeId)
        {
            if (!_ampByChannel.TryGetValue(channelId, out var byNode)) return 0f;
            return byNode.TryGetValue(nodeId, out var v) ? v : 0f;
        }

        public void Set(string channelId, string nodeId, float value)
        {
            TouchChannel(channelId);
            var byNode = _ampByChannel[channelId];
            if (Math.Abs(value) < 0.0001f)
                byNode.Remove(nodeId);
            else
                byNode[nodeId] = value;
        }

        public void Add(string channelId, string nodeId, float delta)
        {
            if (Math.Abs(delta) < 0.0001f) return;
            float v = Get(channelId, nodeId) + delta;
            Set(channelId, nodeId, v);
        }

        public Dictionary<string, float> GetChannelMap(string channelId)
        {
            TouchChannel(channelId);
            return _ampByChannel[channelId];
        }

        public List<string> GetActiveChannelsSorted()
        {
            var list = new List<string>(_activeChannels);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
        
        public void Clear()
        {
            _ampByChannel.Clear();
            _activeChannels.Clear();
        }
    }
}
