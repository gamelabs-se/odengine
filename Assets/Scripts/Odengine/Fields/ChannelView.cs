using System;

namespace Odengine.Fields
{
    /// <summary>
    /// Lightweight facade for a single channel of a ScalarField.
    /// No state, no caching - just delegates to the underlying field.
    /// </summary>
    public readonly struct ChannelView
    {
        private readonly ScalarField _field;
        private readonly string _channelId;

        public ChannelView(ScalarField field, string channelId)
        {
            _field = field ?? throw new ArgumentNullException(nameof(field));
            _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
        }

        public float GetMultiplier(string nodeId) => _field.GetMultiplier(nodeId, _channelId);
        public float GetLogAmp(string nodeId) => _field.GetLogAmp(nodeId, _channelId);
        public void AddLogAmp(string nodeId, float deltaLogAmp) => _field.AddLogAmp(nodeId, _channelId, deltaLogAmp);
        public void SetLogAmp(string nodeId, float logAmp) => _field.SetLogAmp(nodeId, _channelId, logAmp);
    }
}
