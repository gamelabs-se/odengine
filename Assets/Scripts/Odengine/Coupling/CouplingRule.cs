using System;

namespace Odengine.Coupling
{
    /// <summary>
    /// Declares a single cross-field coupling: for each active (node, inputChannel) in
    /// <see cref="SourceFieldId"/>, compute a logAmp delta via <see cref="Operator"/> and
    /// inject it into one or more channels of <see cref="TargetFieldId"/> at the same node.
    ///
    /// No semantic IDs are encoded here — fieldIds and channel selectors are all supplied
    /// by the game layer.  Odengine core has no knowledge of "water", "ore", or "red faction".
    ///
    /// Channel selector grammar
    /// ──────────────────────────────────────────────────────────────────────────
    ///  Input selectors   (which channels to read from the source field at each node):
    ///    "*"                → all active channels at the node (sparse, epsilon-filtered)
    ///    "explicit:[a,b]"   → only the listed channels, filtered by epsilon
    ///    "bare string"      → single named channel, skipped if below epsilon
    ///
    ///  Output selectors  (which channels to write to in the target field at each node):
    ///    "same"             → same channelId as the currently-processed input channel
    ///    "*"                → all currently-active channels in the target field at that node
    ///    "explicit:[a,b]"   → these specific channels (written even if not yet active)
    ///    "bare string"      → single named channel (written even if not yet active)
    ///
    /// The distinction matters: "*" output only touches channels that already exist in the
    /// target; explicit output can create new entries (useful for driving dormant channels).
    ///
    /// ScaleByDeltaTime
    /// ──────────────────────────────────────────────────────────────────────────
    ///  true  (default) → output = Operator.Apply(input) × deltaTime
    ///                    Use for continuous rates (e.g., war erodes economy at 0.15/sec).
    ///  false           → output = Operator.Apply(input)
    ///                    Use for one-shot impulses per Step call (e.g., Threshold triggers).
    /// </summary>
    [Serializable]
    public sealed class CouplingRule
    {
        /// <summary>FieldId of the field to read from. Must exist in the Dimension.</summary>
        public string SourceFieldId { get; set; }

        /// <summary>FieldId of the field to inject into. Must exist in the Dimension.</summary>
        public string TargetFieldId { get; set; }

        /// <summary>
        /// Which channels to read at each source node.
        /// Default: <c>"*"</c> (all active channels).
        /// </summary>
        public string InputChannelSelector { get; set; } = "*";

        /// <summary>
        /// Which channels to write at the same node in the target field.
        /// Default: <c>"same"</c> (same channelId as input).
        /// </summary>
        public string OutputChannelSelector { get; set; } = "same";

        /// <summary>The math transform applied to the input logAmp.</summary>
        public CouplingOperator Operator { get; set; } = CouplingOperator.Linear(1f);

        /// <summary>
        /// When <c>true</c> the raw operator output is multiplied by deltaTime so the
        /// coupling behaves as a continuous rate.  Set to <c>false</c> for impulse-style
        /// coupling that should not depend on tick duration.
        /// </summary>
        public bool ScaleByDeltaTime { get; set; } = true;

        public CouplingRule(string sourceFieldId, string targetFieldId)
        {
            SourceFieldId = sourceFieldId ?? throw new ArgumentNullException(nameof(sourceFieldId));
            TargetFieldId = targetFieldId ?? throw new ArgumentNullException(nameof(targetFieldId));
        }
    }
}
