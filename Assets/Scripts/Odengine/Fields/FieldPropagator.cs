using System;
using System.Collections.Generic;
using Odengine.Graph;

namespace Odengine.Fields
{
    /// <summary>
    /// Two-phase propagation:
    /// 1. Read from current amplitudes
    /// 2. Accumulate deltas into buffer
    /// 3. Apply deltas at end of tick
    /// 
    /// This guarantees order-independence and determinism.
    /// </summary>
    public static class FieldPropagator
    {
        public static Dictionary<string, float> Step(Field field, NodeGraph graph, float dt)
        {
            var deltas = new Dictionary<string, float>();

            // Process nodes in sorted order for determinism
            foreach (var nodeId in graph.GetSortedNodeIds())
            {
                if (!graph.TryGetNode(nodeId, out var node)) continue;

                float sourceAmp = field.GetAmplitude(nodeId);
                if (sourceAmp < field.Profile.MinAmp) continue;

                // Decay
                float decay = sourceAmp * field.Profile.DecayRate * dt;
                if (decay > 0)
                    AccumulateDelta(deltas, nodeId, -decay);

                // Propagate to neighbors (sorted edges for determinism)
                foreach (var edge in node.GetSortedEdges())
                {
                    float transmitted = ComputeTransmission(sourceAmp, edge, field.Profile, dt);
                    if (transmitted < field.Profile.MinAmp) continue;

                    AccumulateDelta(deltas, edge.To.Id, transmitted);

                    // If diffusion mode, subtract from source
                    if (field.Profile.Mode == ConservationMode.Diffusion)
                    {
                        AccumulateDelta(deltas, nodeId, -transmitted);
                    }
                }
            }

            return deltas;
        }

        private static void AccumulateDelta(Dictionary<string, float> deltas, string nodeId, float delta)
        {
            if (!deltas.ContainsKey(nodeId))
                deltas[nodeId] = 0f;
            deltas[nodeId] += delta;
        }

        private static float ComputeTransmission(float sourceAmp, Edge edge, FieldProfile profile, float dt)
        {
            float baseResistance = edge.Resistance * profile.EdgeResistanceScale;
            float tagMultiplier = profile.GetTagMultiplier(edge.Tags);
            float R_eff = baseResistance * tagMultiplier;

            float attenuation = MathF.Exp(-R_eff);
            return sourceAmp * attenuation * profile.PropagationRate * dt;
        }

        /// <summary>
        /// Step a ScalarField (channelized field with base amplitude + layers)
        /// Propagates both base field and all active channels
        /// </summary>
        public static void Step(ScalarField field, NodeGraph graph, float dt)
        {
            // Propagate base field amplitude
            var baseDeltas = new Dictionary<string, float>();
            foreach (var nodeId in graph.GetSortedNodeIds())
            {
                if (!graph.TryGetNode(nodeId, out var node)) continue;

                float sourceAmp = field.GetFieldAmp(nodeId);
                if (sourceAmp < field.Profile.MinAmp) continue;

                // Decay
                float decay = sourceAmp * field.Profile.DecayRate * dt;
                if (decay > 0)
                    AccumulateDelta(baseDeltas, nodeId, -decay);

                // Propagate to neighbors
                foreach (var edge in node.GetSortedEdges())
                {
                    float transmitted = ComputeTransmission(sourceAmp, edge, field.Profile, dt);
                    if (transmitted < field.Profile.MinAmp) continue;

                    AccumulateDelta(baseDeltas, edge.To.Id, transmitted);

                    if (field.Profile.Mode == ConservationMode.Diffusion)
                        AccumulateDelta(baseDeltas, nodeId, -transmitted);
                }
            }

            // Apply base deltas
            foreach (var kvp in baseDeltas)
            {
                float current = field.GetFieldAmp(kvp.Key);
                field.SetFieldAmp(kvp.Key, current + kvp.Value);
            }

            // Propagate each active channel
            var channels = field.Storage.GetActiveChannelsSorted();
            foreach (var channelId in channels)
            {
                var channelDeltas = new Dictionary<string, float>();

                foreach (var nodeId in graph.GetSortedNodeIds())
                {
                    if (!graph.TryGetNode(nodeId, out var node)) continue;

                    float sourceAmp = field.Storage.Get(channelId, nodeId);
                    if (sourceAmp < field.Profile.MinAmp) continue;

                    // Get channel-specific profile overrides
                    var ov = field.ChannelProfileProvider?.GetOverride(channelId);
                    float decayRate = ov?.DecayRate ?? field.Profile.DecayRate;
                    float propRate = ov?.PropagationRate ?? field.Profile.PropagationRate;

                    // Decay
                    float decay = sourceAmp * decayRate * dt;
                    if (decay > 0)
                        AccumulateDelta(channelDeltas, nodeId, -decay);

                    // Propagate to neighbors
                    foreach (var edge in node.GetSortedEdges())
                    {
                        float resScale = ov?.EdgeResistanceScale ?? field.Profile.EdgeResistanceScale;
                        float baseResistance = edge.Resistance * resScale;
                        float tagMultiplier = field.Profile.GetTagMultiplier(edge.Tags);
                        float R_eff = baseResistance * tagMultiplier;

                        float attenuation = MathF.Exp(-R_eff);
                        float transmitted = sourceAmp * attenuation * propRate * dt;

                        if (transmitted < field.Profile.MinAmp) continue;

                        AccumulateDelta(channelDeltas, edge.To.Id, transmitted);

                        if (field.Profile.Mode == ConservationMode.Diffusion)
                            AccumulateDelta(channelDeltas, nodeId, -transmitted);
                    }
                }

                // Apply channel deltas
                foreach (var kvp in channelDeltas)
                    field.Storage.Add(channelId, kvp.Key, kvp.Value);
            }
        }
    }
}
