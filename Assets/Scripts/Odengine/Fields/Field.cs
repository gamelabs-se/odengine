using System;
using System.Collections.Generic;
using Odengine.Core;
using Odengine.Graph;

namespace Odengine.Fields
{
    /// <summary>
    /// A scalar field that exists over the node graph.
    /// Stores Amplitude (float) at each node.
    /// Does NOT store objects, items, or entities.
    /// </summary>
    public sealed class OdField
    {
        public string FieldId { get; }
        public FieldProfile Profile { get; }
        
        private readonly Dictionary<string, float> _amplitudes;

        public OdField(string fieldId, FieldProfile profile)
        {
            FieldId = fieldId;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _amplitudes = new Dictionary<string, float>();
        }

        public float GetAmplitude(string nodeId)
        {
            return _amplitudes.TryGetValue(nodeId, out var amp) ? amp : 0f;
        }

        public void SetAmplitude(string nodeId, float amplitude)
        {
            _amplitudes[nodeId] = amplitude;
        }

        public void ModifyAmplitude(string nodeId, float delta)
        {
            var current = GetAmplitude(nodeId);
            SetAmplitude(nodeId, current + delta);
        }

        /// <summary>
        /// Apply deltas from propagation step (two-phase pattern).
        /// </summary>
        public void ApplyDeltas(Dictionary<string, float> deltas)
        {
            foreach (var kvp in deltas)
            {
                ModifyAmplitude(kvp.Key, kvp.Value);
            }
        }

        public IEnumerable<(string nodeId, float amp)> GetAllAmplitudes()
        {
            foreach (var kvp in _amplitudes)
            {
                yield return (kvp.Key, kvp.Value);
            }
        }

        public void Clear() => _amplitudes.Clear();

        public override string ToString() => $"Field({FieldId}, nodes={_amplitudes.Count})";
    }
}
