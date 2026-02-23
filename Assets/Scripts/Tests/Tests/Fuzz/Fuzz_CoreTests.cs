using System;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;
using Odengine.Tests.Shared;

namespace Odengine.Tests.Fuzz
{
    /// <summary>
    /// Fuzz tests using <see cref="DeterministicRng"/> (xorshift32).
    ///
    /// Each test documents its seed so failures are perfectly reproducible.
    /// Invariants checked every tick:
    ///   - No NaN logAmps
    ///   - No Infinity logAmps
    ///   - All values within MinLogAmpClamp / MaxLogAmpClamp
    ///   - GetMultiplier always > 0
    /// </summary>
    [TestFixture]
    public class Fuzz_CoreTests
    {
        private static FieldProfile MakeProfile(string id = "fuzz") =>
            new FieldProfile(id)
            {
                PropagationRate = 0.5f,
                EdgeResistanceScale = 1f,
                DecayRate = 0.1f,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            };

        private static void AssertFieldHealthy(ScalarField field, string context)
        {
            foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
            {
                Assert.IsFalse(float.IsNaN(logAmp),
                    $"[{context}] NaN logAmp at node={nodeId} channel={channelId}");
                Assert.IsFalse(float.IsInfinity(logAmp),
                    $"[{context}] Infinity logAmp at node={nodeId} channel={channelId}");
                Assert.GreaterOrEqual(logAmp, field.Profile.MinLogAmpClamp - 1e-3f,
                    $"[{context}] Below MinLogAmpClamp at node={nodeId} channel={channelId}");
                Assert.LessOrEqual(logAmp, field.Profile.MaxLogAmpClamp + 1e-3f,
                    $"[{context}] Above MaxLogAmpClamp at node={nodeId} channel={channelId}");
                Assert.Greater(field.GetMultiplier(nodeId, channelId), 0f,
                    $"[{context}] Multiplier must be positive");
            }
        }

        private static (Dimension, ScalarField) RunFuzzScenario(
            uint seed, int nodeCount, int edgeCount, int channels,
            int steps, float dt = 0.1f)
        {
            var rng = new DeterministicRng(seed);
            var dim = TestBuilders.BuildRandomDimension(rng, nodeCount, edgeCount);
            var field = dim.AddField("fuzz", MakeProfile());

            var nodeIds = dim.Graph.GetNodeIdsSorted();
            int n = nodeIds.Count;
            if (n == 0) return (dim, field);

            // Random initial impulses
            for (int i = 0; i < nodeCount * channels; i++)
            {
                string nodeId = nodeIds[rng.NextInt(0, n)];
                string channelId = $"ch{rng.NextInt(0, channels):000}";
                field.AddLogAmp(nodeId, channelId, rng.NextFloat(-3f, 3f));
            }

            for (int step = 0; step < steps; step++)
            {
                Propagator.Step(dim, field, dt);
                AssertFieldHealthy(field, $"seed={seed} step={step}");
            }

            return (dim, field);
        }

        // ─── No NaN / Inf across several seeds ───────────────────────────────

        [Test] public void Fuzz_Seed1_NoNaN_NoInfinity() => RunFuzzScenario(1, 10, 20, 5, 50);
        [Test] public void Fuzz_Seed2_NoNaN_NoInfinity() => RunFuzzScenario(2, 15, 30, 8, 50);
        [Test] public void Fuzz_Seed3_NoNaN_NoInfinity() => RunFuzzScenario(3, 20, 40, 3, 50);
        [Test] public void Fuzz_Seed42_NoNaN_NoInfinity() => RunFuzzScenario(42, 8, 16, 6, 50);
        [Test] public void Fuzz_Seed999_NoNaN_NoInfinity() => RunFuzzScenario(999, 12, 25, 4, 100);

        // ─── Hash stability — same seed produces same hash ────────────────────

        [Test]
        public void Fuzz_Seed1_HashStableAcrossTwoRuns()
        {
            var (d1, _) = RunFuzzScenario(1, 10, 20, 5, 20);
            var (d2, _) = RunFuzzScenario(1, 10, 20, 5, 20);
            Assert.AreEqual(StateHash.Compute(d1), StateHash.Compute(d2));
        }

        [Test]
        public void Fuzz_Seed7_HashStableAcrossTwoRuns()
        {
            var (d1, _) = RunFuzzScenario(7, 8, 15, 4, 30);
            var (d2, _) = RunFuzzScenario(7, 8, 15, 4, 30);
            Assert.AreEqual(StateHash.Compute(d1), StateHash.Compute(d2));
        }

        [Test]
        public void Fuzz_Seed100_HashStableAcrossTwoRuns()
        {
            var (d1, _) = RunFuzzScenario(100, 12, 24, 5, 50);
            var (d2, _) = RunFuzzScenario(100, 12, 24, 5, 50);
            Assert.AreEqual(StateHash.Compute(d1), StateHash.Compute(d2));
        }

        // ─── Different seeds produce different hashes ─────────────────────────

        [Test]
        public void Fuzz_DifferentSeeds_ProduceDifferentHashes()
        {
            var (d1, _) = RunFuzzScenario(1, 10, 20, 5, 20);
            var (d2, _) = RunFuzzScenario(2, 10, 20, 5, 20);
            Assert.AreNotEqual(StateHash.Compute(d1), StateHash.Compute(d2));
        }

        // ─── Specific topologies ──────────────────────────────────────────────

        [Test]
        public void Fuzz_StarGraph_NoExplosion()
        {
            uint seed = 55;
            var rng = new DeterministicRng(seed);
            int leafCount = 20;

            var dim = new Dimension();
            dim.AddNode("hub");
            for (int i = 0; i < leafCount; i++) dim.AddNode($"leaf{i:00}");

            // Hub → all leaves
            for (int i = 0; i < leafCount; i++) dim.AddEdge("hub", $"leaf{i:00}", 0.5f);

            var field = dim.AddField("fuzz", MakeProfile());
            field.SetLogAmp("hub", "ch", 5f);

            for (int step = 0; step < 100; step++)
            {
                Propagator.Step(dim, field, 0.1f);
                AssertFieldHealthy(field, $"StarGraph step={step}");
            }
        }

        [Test]
        public void Fuzz_DenseGraph_NoExplosion()
        {
            uint seed = 77;
            var rng = new DeterministicRng(seed);
            // Dense: 10 nodes with 45+ edges (near-complete graph)
            var (dim, field) = RunFuzzScenario(seed, 10, 45, 3, 100);
            // If we get here without assertion errors, test passes
        }

        [Test]
        public void Fuzz_ManyChannels_NoExplosion()
        {
            uint seed = 88;
            var rng = new DeterministicRng(seed);
            var (dim, field) = RunFuzzScenario(seed, 8, 12, 20, 50);
        }

        // ─── Clamp invariant ──────────────────────────────────────────────────

        [Test]
        public void Fuzz_AllValuesAlwaysWithinClampBounds()
        {
            // High propagation with zero decay → stress-test clamping
            uint seed = 13;
            var rng = new DeterministicRng(seed);
            var dim = TestBuilders.BuildRandomDimension(rng, 12, 24);
            var profile = new FieldProfile("clamp-stress")
            {
                PropagationRate = 5f,   // very high
                DecayRate = 0f,
                MinLogAmpClamp = -10f,
                MaxLogAmpClamp = 10f
            };
            var field = dim.AddField("cs", profile);
            var nodeIds = dim.Graph.GetNodeIdsSorted();
            if (nodeIds.Count == 0) return;

            // Large initial impulses
            for (int i = 0; i < 30; i++)
            {
                string n = nodeIds[rng.NextInt(0, nodeIds.Count)];
                field.AddLogAmp(n, "stress", rng.NextFloat(-50f, 50f));
            }

            for (int step = 0; step < 100; step++)
            {
                Propagator.Step(dim, field, 0.5f);
                AssertFieldHealthy(field, $"ClampStress step={step}");
            }
        }

        // ─── Decay convergence ────────────────────────────────────────────────

        [Test]
        public void Fuzz_HighDecay_ConvergesToNeutral()
        {
            var dim = new Dimension();
            dim.AddNode("a"); dim.AddNode("b");
            dim.AddEdge("a", "b", 1f);

            var profile = new FieldProfile("decay-test")
            {
                PropagationRate = 0.1f,
                DecayRate = 2f,         // very high decay
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            };
            var field = dim.AddField("dt", profile);
            field.SetLogAmp("a", "ch", 10f);
            field.SetLogAmp("b", "ch", -5f);

            for (int step = 0; step < 200; step++)
                Propagator.Step(dim, field, 0.1f);

            Assert.AreEqual(0f, field.GetLogAmp("a", "ch"), 0.5f,
                "With high decay, amplitude must converge near neutral");
            Assert.AreEqual(0f, field.GetLogAmp("b", "ch"), 0.5f,
                "With high decay, amplitude must converge near neutral");
        }

        // ─── Isolated node invariant ──────────────────────────────────────────

        [Test]
        public void Fuzz_IsolatedNode_NeverReceivesFromOtherNodes()
        {
            var rng = new DeterministicRng(200);
            int nodeCount = 10;
            // Build a network but leave "island" completely disconnected
            var dim = new Dimension();
            dim.AddNode("island");
            for (int i = 0; i < nodeCount; i++) dim.AddNode($"n{i}");
            for (int i = 0; i < nodeCount - 1; i++) dim.AddEdge($"n{i}", $"n{i + 1}", 0.5f);

            var field = dim.AddField("fuzz", MakeProfile());

            // Inject signal everywhere except island
            for (int i = 0; i < nodeCount; i++)
                field.SetLogAmp($"n{i}", "ch", rng.NextFloat(-2f, 2f));

            for (int step = 0; step < 50; step++)
                Propagator.Step(dim, field, 0.1f);

            Assert.AreEqual(0f, field.GetLogAmp("island", "ch"), 1e-9f,
                "Isolated node must never receive signal from the connected network");
        }
    }
}
