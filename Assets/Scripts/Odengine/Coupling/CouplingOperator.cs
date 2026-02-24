using System;

namespace Odengine.Coupling
{
    /// <summary>
    /// The mathematical transform applied to a sampled source logAmp to produce the
    /// logAmp delta injected into the target field.
    ///
    /// Kinds:
    ///   Linear    — output = input × Scale + Bias
    ///   Clamp     — output = clamp(input, Min, Max) × Scale
    ///   Threshold — output = input > Threshold ? ImpulseValue : 0
    ///   Ratio     — output = (input / Reference) × Scale
    ///
    /// All kinds operate in log-space: both input and output are logAmp values.
    /// No semantic meaning is encoded here — the caller decides the sign and scale.
    /// </summary>
    public enum CouplingOperatorKind
    {
        /// <summary>output = input × Scale + Bias</summary>
        Linear,

        /// <summary>output = clamp(input, Min, Max) × Scale</summary>
        Clamp,

        /// <summary>output = input > Threshold ? ImpulseValue : 0  (no dt scale for impulse)</summary>
        Threshold,

        /// <summary>output = (input / Reference) × Scale  (0 if Reference ≈ 0)</summary>
        Ratio,
    }

    /// <summary>
    /// Immutable-by-convention data record describing a single math transform.
    ///
    /// Use the static factory methods for clarity:
    /// <code>
    ///   CouplingOperator.Linear(-0.15f)
    ///   CouplingOperator.Threshold(1.0f, impulseValue: -0.5f)
    /// </code>
    /// </summary>
    [Serializable]
    public sealed class CouplingOperator
    {
        public CouplingOperatorKind Kind { get; set; } = CouplingOperatorKind.Linear;
        public float Scale { get; set; } = 1f;
        public float Bias { get; set; } = 0f;
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 1f;
        public float ThresholdValue { get; set; } = 0f;
        public float ImpulseValue { get; set; } = 1f;
        public float Reference { get; set; } = 1f;

        /// <summary>
        /// Compute the raw output logAmp delta from <paramref name="input"/>.
        /// <c>CouplingProcessor</c> multiplies by <c>deltaTime</c> when
        /// <see cref="CouplingRule.ScaleByDeltaTime"/> is true.
        /// </summary>
        public float Apply(float input) => Kind switch
        {
            CouplingOperatorKind.Linear => input * Scale + Bias,
            CouplingOperatorKind.Clamp => Math.Clamp(input, Min, Max) * Scale,
            CouplingOperatorKind.Threshold => input > ThresholdValue ? ImpulseValue : 0f,
            CouplingOperatorKind.Ratio => MathF.Abs(Reference) < 1e-9f
                                                 ? 0f
                                                 : (input / Reference) * Scale,
            _ => 0f,
        };

        // ── Factories ──────────────────────────────────────────────────────────

        /// <summary>output = input × <paramref name="scale"/> + <paramref name="bias"/></summary>
        public static CouplingOperator Linear(float scale, float bias = 0f)
            => new CouplingOperator { Kind = CouplingOperatorKind.Linear, Scale = scale, Bias = bias };

        /// <summary>output = clamp(input, <paramref name="min"/>, <paramref name="max"/>) × <paramref name="scale"/></summary>
        public static CouplingOperator Clamp(float min, float max, float scale = 1f)
            => new CouplingOperator { Kind = CouplingOperatorKind.Clamp, Min = min, Max = max, Scale = scale };

        /// <summary>output = input > <paramref name="threshold"/> ? <paramref name="impulseValue"/> : 0</summary>
        public static CouplingOperator Threshold(float threshold, float impulseValue)
            => new CouplingOperator
            {
                Kind = CouplingOperatorKind.Threshold,
                ThresholdValue = threshold,
                ImpulseValue = impulseValue,
            };

        /// <summary>output = (input / <paramref name="reference"/>) × <paramref name="scale"/></summary>
        public static CouplingOperator Ratio(float reference, float scale = 1f)
            => new CouplingOperator { Kind = CouplingOperatorKind.Ratio, Reference = reference, Scale = scale };
    }
}
