using System;
using System.Collections.Generic;
using System.Linq;
using Odengine.Core;
using Odengine.Graph;

namespace Odengine.Fields
{
    /// <summary>
    /// Deterministic double-buffered field propagation.
    /// No in-place mutation during neighbor iteration.
    /// </summary>
    public static class Propagator
    {
        public static void Step(
            Dimension dimension,
            ScalarField field,
            float deltaTime,
            string requiredEdgeTag = null)
        {
            if (dimension == null) throw new ArgumentNullException(nameof(dimension));
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (deltaTime <= 0f) return;

            var graph = dimension.Graph;
            var profile = field.Profile;

            // Accumulator for deltas (deterministic sorted application later)
            var deltas = new Dictionary<(string nodeId, string channelId), float>();

            // Process each active channel
            var channels = field.GetActiveChannelIdsSorted();
            UnityEngine.Debug.Log($"[Propagator] Active channels: {channels.Count}");
            foreach (var channelId in channels)
            {
                var activeNodes = field.GetActiveNodeIdsSortedForChannel(channelId);
                UnityEngine.Debug.Log($"[Propagator] Channel '{channelId}' has {activeNodes.Count} active nodes");

                foreach (var nodeId in activeNodes)
                {
                    float sourceLogAmp = field.GetLogAmp(nodeId, channelId);
                    UnityEngine.Debug.Log($"[Propagator] Node '{nodeId}' logAmp: {sourceLogAmp}");
                    if (MathF.Abs(sourceLogAmp) < field.Profile.LogEpsilon)
                        continue;

                    var edges = graph.GetOutEdgesSorted(nodeId);
                    UnityEngine.Debug.Log($"[Propagator] Node '{nodeId}' has {edges.Count} out edges");

                    foreach (var edge in edges)
                    {
                        // Filter by tag if required
                        if (requiredEdgeTag != null && !edge.HasTag(requiredEdgeTag))
                            continue;

                        // Compute transmission
                        float effectiveResistance = edge.Resistance * profile.EdgeResistanceScale;
                        float transmissionFactor = MathF.Exp(-effectiveResistance);
                        float transmittedLogAmpDelta = sourceLogAmp * transmissionFactor * profile.PropagationRate * deltaTime;

                        if (MathF.Abs(transmittedLogAmpDelta) < field.Profile.LogEpsilon)
                            continue;

                        // Accumulate delta at destination
                        var destKey = (edge.ToId, channelId);
                        deltas.TryGetValue(destKey, out float existing);
                        deltas[destKey] = existing + transmittedLogAmpDelta;
                    }

                    // Apply decay at source
                    if (profile.DecayRate > 0f)
                    {
                        float decayDelta = -sourceLogAmp * profile.DecayRate * deltaTime;
                        var sourceKey = (nodeId, channelId);
                        deltas.TryGetValue(sourceKey, out float existing);
                        deltas[sourceKey] = existing + decayDelta;
                    }
                }
            }

            // Apply deltas deterministically (sorted order)
            var sortedKeys = deltas.Keys
                .OrderBy(k => k.channelId, StringComparer.Ordinal)
                .ThenBy(k => k.nodeId, StringComparer.Ordinal)
                .ToList();

            foreach (var key in sortedKeys)
            {
                float delta = deltas[key];
                field.AddLogAmp(key.nodeId, key.channelId, delta);
            }
        }
    }
}
