using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Tests.Shared
{
    /// <summary>
    /// Factory helpers for common test topologies and profiles.
    /// Keep IDs generic — no semantic meaning in helpers.
    /// </summary>
    public static class TestBuilders
    {
        // ── Profiles ────────────────────────────────────────────────────────

        /// <summary>PropagationRate=1, EdgeResistanceScale=1, DecayRate=0.</summary>
        public static FieldProfile DefaultProfile(string id = "test") =>
            new FieldProfile(id)
            {
                PropagationRate = 1f,
                EdgeResistanceScale = 1f,
                DecayRate = 0f
            };

        /// <summary>Fast propagation, no decay.</summary>
        public static FieldProfile FastProfile(string id = "fast") =>
            new FieldProfile(id)
            {
                PropagationRate = 2f,
                EdgeResistanceScale = 0.1f,
                DecayRate = 0f
            };

        /// <summary>Slow propagation with moderate decay.</summary>
        public static FieldProfile SlowDecayProfile(string id = "slow") =>
            new FieldProfile(id)
            {
                PropagationRate = 0.1f,
                EdgeResistanceScale = 1f,
                DecayRate = 0.05f
            };

        /// <summary>High decay — amplitude collapses quickly.</summary>
        public static FieldProfile HighDecayProfile(string id = "highdecay") =>
            new FieldProfile(id)
            {
                PropagationRate = 0.5f,
                EdgeResistanceScale = 1f,
                DecayRate = 0.5f
            };

        // ── Graph topologies ────────────────────────────────────────────────

        /// <summary>Linear chain: n0 → n1 → n2 → ... with resistance=1.</summary>
        public static Dimension LinearChain(params string[] nodeIds)
        {
            var dim = new Dimension();
            foreach (var id in nodeIds)
                dim.AddNode(id);
            for (int i = 0; i < nodeIds.Length - 1; i++)
                dim.AddEdge(nodeIds[i], nodeIds[i + 1], 1.0f);
            return dim;
        }

        /// <summary>Star: hub with outgoing edges to each leaf.</summary>
        public static Dimension StarGraph(string hub, string[] leaves,
            float resistance = 1f, string[] tags = null)
        {
            var dim = new Dimension();
            dim.AddNode(hub);
            foreach (var leaf in leaves)
            {
                dim.AddNode(leaf);
                if (tags == null || tags.Length == 0)
                    dim.AddEdge(hub, leaf, resistance);
                else
                    dim.AddEdge(hub, leaf, resistance, tags);
            }
            return dim;
        }

        /// <summary>Two nodes connected in both directions.</summary>
        public static Dimension Bidirectional(string a, string b, float resistance = 1f)
        {
            var dim = new Dimension();
            dim.AddNode(a);
            dim.AddNode(b);
            dim.AddEdge(a, b, resistance);
            dim.AddEdge(b, a, resistance);
            return dim;
        }

        /// <summary>Random dimension for fuzz testing.</summary>
        public static Dimension BuildRandomDimension(DeterministicRng rng,
            int nodeCount, int edgeCount)
        {
            var dim = new Dimension();
            var ids = new string[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                ids[i] = $"n{i}";
                dim.AddNode(ids[i]);
            }
            for (int i = 0; i < edgeCount; i++)
            {
                int from = rng.NextInt(0, nodeCount);
                int to = rng.NextInt(0, nodeCount);
                if (from == to) continue;
                float r = rng.NextFloat(0f, 5f);
                try { dim.AddEdge(ids[from], ids[to], r); }
                catch { /* ignore duplicate-edge errors if any */ }
            }
            return dim;
        }
    }
}
