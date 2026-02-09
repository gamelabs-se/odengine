using System;
using System.Collections.Generic;
using Odengine.Core;
using Odengine.Graph;

namespace Odengine.Fields
{
    /// <summary>
    /// Samples a field at a node to produce concrete, observable values.
    /// Sampling is deterministic and does NOT mutate the field.
    /// The game layer uses samplers to "realize" amplitudes into game concepts.
    /// </summary>
    public abstract class FieldSampler
    {
        public string SamplerId { get; }

        protected FieldSampler(string samplerId)
        {
            SamplerId = samplerId;
        }

        /// <summary>
        /// Sample the field at a specific node.
        /// Returns a deterministic value based on amplitude.
        /// </summary>
        public abstract object Sample(OdNode node, OdField field, Dictionary<string, object> context = null);
    }

    /// <summary>
    /// Generic typed sampler for cleaner usage.
    /// </summary>
    public abstract class FieldSampler<T> : FieldSampler
    {
        protected FieldSampler(string samplerId) : base(samplerId) { }

        public abstract T SampleTyped(OdNode node, OdField field, Dictionary<string, object> context = null);

        public sealed override object Sample(OdNode node, OdField field, Dictionary<string, object> context = null)
        {
            return SampleTyped(node, field, context);
        }
    }

    /// <summary>
    /// Example: Simple linear sampler that just returns amplitude as a float.
    /// </summary>
    public sealed class LinearSampler : FieldSampler<float>
    {
        public float Scale { get; set; }

        public LinearSampler(string id, float scale = 1f) : base(id)
        {
            Scale = scale;
        }

        public override float SampleTyped(OdNode node, OdField field, Dictionary<string, object> context = null)
        {
            var amp = field.GetAmplitude(node.Id);
            return amp * Scale;
        }
    }

    /// <summary>
    /// Example: Clamped sampler with min/max bounds.
    /// </summary>
    public sealed class ClampedSampler : FieldSampler<float>
    {
        public float Min { get; set; }
        public float Max { get; set; }

        public ClampedSampler(string id, float min = 0f, float max = 1f) : base(id)
        {
            Min = min;
            Max = max;
        }

        public override float SampleTyped(OdNode node, OdField field, Dictionary<string, object> context = null)
        {
            var amp = field.GetAmplitude(node.Id);
            return Math.Clamp(amp, Min, Max);
        }
    }
}
