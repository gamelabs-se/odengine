using System;
using System.Collections.Generic;
using System.Linq;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Coupling
{
    /// <summary>
    /// Deterministic, double-buffered cross-field coupling engine.
    ///
    /// Call once per simulation tick, after all Propagator.Step calls:
    /// <code>
    ///   CouplingProcessor.Step(dimension, rules, deltaTime);
    /// </code>
    ///
    /// Determinism guarantees
    /// ──────────────────────────────────────────────────────────────────────────
    /// • All source reads happen before any target writes (true double-buffer).
    ///   Self-coupling (source == target) is therefore safe.
    /// • Rules are processed in declaration order (caller controls priority).
    /// • Within each rule, nodes are iterated in Ordinal-sorted order.
    /// • Accumulated deltas are applied sorted by (fieldId, channelId, nodeId).
    ///
    /// Missing fields are silently skipped — rules can safely be declared at
    /// startup even if both systems are not yet initialised.
    /// </summary>
    public static class CouplingProcessor
    {
        public static void Step(
            Dimension dimension,
            IReadOnlyList<CouplingRule> rules,
            float deltaTime)
        {
            if (dimension == null) throw new ArgumentNullException(nameof(dimension));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (rules.Count == 0 || deltaTime <= 0f) return;

            // ── Accumulation phase (reads only) ───────────────────────────────
            var deltas = new Dictionary<(string fieldId, string nodeId, string channelId), float>();

            foreach (var rule in rules)
            {
                var sourceField = dimension.GetField(rule.SourceFieldId);
                var targetField = dimension.GetField(rule.TargetFieldId);
                if (sourceField == null || targetField == null) continue;

                float epsilon = sourceField.Profile.LogEpsilon;

                // Iterate active nodes in source field (deterministic sorted order)
                var activeNodes = sourceField.GetActiveNodeIdsSorted();

                foreach (var nodeId in activeNodes)
                {
                    var inputChannels = ResolveInputChannels(sourceField, nodeId,
                                                             rule.InputChannelSelector);

                    foreach (var inputChannelId in inputChannels)
                    {
                        float inputLogAmp = sourceField.GetLogAmp(nodeId, inputChannelId);
                        float rawOutput = rule.Operator.Apply(inputLogAmp);
                        float output = rule.ScaleByDeltaTime ? rawOutput * deltaTime : rawOutput;

                        if (MathF.Abs(output) < epsilon) continue;

                        var outputChannels = ResolveOutputChannels(targetField, nodeId,
                                                                    inputChannelId,
                                                                    rule.OutputChannelSelector);

                        foreach (var outputChannelId in outputChannels)
                        {
                            var key = (rule.TargetFieldId, nodeId, outputChannelId);
                            deltas.TryGetValue(key, out float existing);
                            deltas[key] = existing + output;
                        }
                    }
                }
            }

            // ── Apply phase (writes only, deterministic sorted order) ──────────
            var sortedKeys = deltas.Keys
                .OrderBy(k => k.fieldId, StringComparer.Ordinal)
                .ThenBy(k => k.channelId, StringComparer.Ordinal)
                .ThenBy(k => k.nodeId, StringComparer.Ordinal)
                .ToList();

            foreach (var key in sortedKeys)
            {
                dimension.GetField(key.fieldId)
                         ?.AddLogAmp(key.nodeId, key.channelId, deltas[key]);
            }
        }

        // ── Channel selector resolution ────────────────────────────────────────

        /// <summary>
        /// Returns the input channels to process at <paramref name="nodeId"/>.
        /// Only channels with |logAmp| ≥ epsilon are included (sparse contract).
        /// </summary>
        private static IReadOnlyList<string> ResolveInputChannels(
            ScalarField field, string nodeId, string selector)
        {
            // "*" → all active channels at this node
            if (selector == "*")
                return field.GetActiveChannelIdsSortedForNode(nodeId);

            // "explicit:[a,b,c]" or "explicit:a,b,c"
            if (TryParseExplicit(selector, out var ids))
            {
                float eps = field.Profile.LogEpsilon;
                var result = new List<string>(ids.Count);
                foreach (var id in ids)
                {
                    if (MathF.Abs(field.GetLogAmp(nodeId, id)) >= eps)
                        result.Add(id);
                }
                result.Sort(StringComparer.Ordinal);
                return result;
            }

            // Bare string → single named channel, only if non-trivially active
            float amp = field.GetLogAmp(nodeId, selector);
            return MathF.Abs(amp) >= field.Profile.LogEpsilon
                ? (IReadOnlyList<string>)new[] { selector }
                : Array.Empty<string>();
        }

        /// <summary>
        /// Returns the output channels to write at <paramref name="nodeId"/> in the target field.
        /// Unlike input resolution, explicit and bare selectors write even to inactive channels
        /// (they create new field entries — this is the coupling injection point).
        /// </summary>
        private static IReadOnlyList<string> ResolveOutputChannels(
            ScalarField targetField, string nodeId, string inputChannelId, string selector)
        {
            // "same" → same channelId as the currently-processed input channel
            if (selector == "same")
                return new[] { inputChannelId };

            // "*" → all currently-active channels in target at this node
            if (selector == "*")
                return targetField.GetActiveChannelIdsSortedForNode(nodeId);

            // "explicit:[a,b,c]" or "explicit:a,b,c" → listed channels (creates entries if absent)
            if (TryParseExplicit(selector, out var ids))
            {
                var sorted = new List<string>(ids);
                sorted.Sort(StringComparer.Ordinal);
                return sorted;
            }

            // Bare string → single named channel (creates entry if absent)
            return new[] { selector };
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static bool TryParseExplicit(string selector, out List<string> ids)
        {
            ids = null;

            // "explicit:[a,b,c]"
            if (selector.StartsWith("explicit:[", StringComparison.Ordinal)
                && selector.EndsWith("]", StringComparison.Ordinal))
            {
                var inner = selector.Substring(
                    "explicit:[".Length,
                    selector.Length - "explicit:[".Length - 1);
                ids = ParseCommaSeparated(inner);
                return true;
            }

            // "explicit:a,b,c"  (no brackets — alternate format)
            if (selector.StartsWith("explicit:", StringComparison.Ordinal))
            {
                var inner = selector.Substring("explicit:".Length);
                ids = ParseCommaSeparated(inner);
                return true;
            }

            return false;
        }

        private static List<string> ParseCommaSeparated(string s)
        {
            var parts = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>(parts.Length);
            foreach (var p in parts)
            {
                var trimmed = p.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
            return result;
        }
    }
}
