using System;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Fields;
using Odengine.Tests.Shared;

namespace Odengine.Tests.Scenarios
{
    /// <summary>
    /// Long-horizon multi-tick scenario tests.
    ///
    /// These tests verify simulation invariants across many ticks:
    ///   - No NaN / Infinity at any tick
    ///   - All logAmps within profile clamp bounds
    ///   - Deterministic hash across two identical runs
    ///   - Domain-level emergent behaviour (price rises, propagation cascades, etc.)
    /// </summary>
    [TestFixture]
    public class Scenarios_LongHorizonTests
    {
        private static FieldProfile TradeProfile(string id = "trade") =>
            new FieldProfile(id)
            {
                PropagationRate = 0.3f,
                EdgeResistanceScale = 1f,
                DecayRate = 0.05f,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            };

        private static FieldProfile EcoProfile() =>
            new FieldProfile("economy")
            {
                PropagationRate = 0.3f,
                DecayRate = 0.05f,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            };

        private static void AssertNoNaNOrInfinity(ScalarField field, int tick)
        {
            foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
            {
                Assert.IsFalse(float.IsNaN(logAmp),
                    $"Tick {tick}: NaN at node={nodeId} channel={channelId}");
                Assert.IsFalse(float.IsInfinity(logAmp),
                    $"Tick {tick}: Infinity at node={nodeId} channel={channelId}");
            }
        }

        private static void AssertWithinClamps(ScalarField field, int tick)
        {
            foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
            {
                Assert.GreaterOrEqual(logAmp, field.Profile.MinLogAmpClamp - 1e-3f,
                    $"Tick {tick}: Below MinLogAmpClamp node={nodeId} ch={channelId}");
                Assert.LessOrEqual(logAmp, field.Profile.MaxLogAmpClamp + 1e-3f,
                    $"Tick {tick}: Above MaxLogAmpClamp node={nodeId} ch={channelId}");
            }
        }

        // ─── 500-tick invariant battery ───────────────────────────────────────

        [Test]
        public void Scenario_500Ticks_NoNaN()
        {
            var dim = new Dimension();
            string[] nodes = { "alpha", "beta", "gamma", "delta", "epsilon" };
            foreach (var n in nodes) dim.AddNode(n);
            dim.AddEdge("alpha", "beta", 0.5f);
            dim.AddEdge("beta", "gamma", 0.5f);
            dim.AddEdge("gamma", "delta", 1f);
            dim.AddEdge("delta", "epsilon", 0.5f);

            var field = dim.AddField("main", TradeProfile());
            field.SetLogAmp("alpha", "ch", 4f);

            for (int tick = 0; tick < 500; tick++)
            {
                Propagator.Step(dim, field, 0.05f);
                AssertNoNaNOrInfinity(field, tick);
            }
        }

        [Test]
        public void Scenario_500Ticks_NoInfinity()
        {
            var dim = new Dimension();
            dim.AddNode("src"); dim.AddNode("dst");
            dim.AddEdge("src", "dst", 0f);

            var field = dim.AddField("f", TradeProfile());
            field.SetLogAmp("src", "ch", 10f);

            for (int tick = 0; tick < 500; tick++)
            {
                Propagator.Step(dim, field, 0.1f);
                AssertNoNaNOrInfinity(field, tick);
            }
        }

        [Test]
        public void Scenario_500Ticks_AllWithinClampBounds()
        {
            var dim = new Dimension();
            string[] nodes = { "a", "b", "c", "d" };
            foreach (var n in nodes) dim.AddNode(n);
            dim.AddEdge("a", "b", 0.5f); dim.AddEdge("b", "c", 0.5f); dim.AddEdge("c", "d", 0.5f);

            var profile = new FieldProfile("clamped")
            {
                PropagationRate = 1f,
                DecayRate = 0f,
                MinLogAmpClamp = -5f,
                MaxLogAmpClamp = 5f
            };
            var field = dim.AddField("f", profile);
            field.SetLogAmp("a", "ch", 20f); // exceeds clamp, should be clamped

            for (int tick = 0; tick < 500; tick++)
            {
                Propagator.Step(dim, field, 0.1f);
                AssertWithinClamps(field, tick);
            }
        }

        [Test]
        public void Scenario_500Ticks_DeterministicHash()
        {
            Dimension Run()
            {
                var dim = new Dimension();
                string[] nodes = { "a", "b", "c" };
                foreach (var n in nodes) dim.AddNode(n);
                dim.AddEdge("a", "b", 0.5f);
                dim.AddEdge("b", "c", 0.5f);
                var f = dim.AddField("f", TradeProfile());
                f.SetLogAmp("a", "main", 3f);
                f.SetLogAmp("a", "secondary", -1f);
                for (int tick = 0; tick < 500; tick++)
                    Propagator.Step(dim, f, 0.05f);
                return dim;
            }

            Assert.AreEqual(StateHash.Compute(Run()), StateHash.Compute(Run()),
                "500-tick run must be deterministic");
        }

        // ─── Neutral system stays neutral ─────────────────────────────────────

        [Test]
        public void Scenario_NeutralSystem_StaysNeutral()
        {
            var dim = new Dimension();
            string[] nodes = { "a", "b", "c", "d" };
            foreach (var n in nodes) dim.AddNode(n);
            dim.AddEdge("a", "b", 0.5f); dim.AddEdge("b", "c", 0.5f); dim.AddEdge("c", "d", 0.5f);

            var field = dim.AddField("f", TradeProfile());
            // No impulses — system is fully neutral

            for (int tick = 0; tick < 100; tick++)
                Propagator.Step(dim, field, 0.1f);

            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count,
                "Neutral system with no impulses must remain neutral after propagation");
        }

        // ─── Single impulse propagates and decays ─────────────────────────────

        [Test]
        public void Scenario_SingleImpulse_PropagatesAlongChain_ThenDecays()
        {
            var dim = new Dimension();
            string[] chain = { "n0", "n1", "n2", "n3", "n4" };
            foreach (var n in chain) dim.AddNode(n);
            for (int i = 0; i < chain.Length - 1; i++)
                dim.AddEdge(chain[i], chain[i + 1], 0f);

            var profile = new FieldProfile("decay")
            {
                PropagationRate = 1f,
                DecayRate = 1f,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            };
            var field = dim.AddField("f", profile);
            field.SetLogAmp("n0", "signal", 5f);

            // Simulate: signal should propagate forward and decay
            for (int tick = 0; tick < 50; tick++)
                Propagator.Step(dim, field, 0.1f);

            // Eventually amplitude should decay to near-neutral
            Assert.AreEqual(0f, field.GetLogAmp("n0", "signal"), 1f,
                "Source signal must decay significantly over 50 ticks");
        }

        // ─── Blockade scenario ────────────────────────────────────────────────

        [Test]
        public void Scenario_HighResistanceBlockade_LimitsRipple()
        {
            var dim = new Dimension();
            dim.AddNode("market"); dim.AddNode("remote");
            dim.AddEdge("market", "remote", 50f); // extreme resistance — blockade

            var field = dim.AddField("f", TradeProfile());
            field.SetLogAmp("market", "food", 8f);

            for (int tick = 0; tick < 100; tick++)
                Propagator.Step(dim, field, 0.1f);

            Assert.AreEqual(0f, field.GetLogAmp("remote", "food"), 0.001f,
                "Blockade (extreme resistance) must prevent meaningful signal spread");
        }

        // ─── Two markets connected ────────────────────────────────────────────

        [Test]
        public void Scenario_TwoMarkets_ImpulseEventuallyReachesSecond()
        {
            var dim = new Dimension();
            dim.AddNode("city"); dim.AddNode("village");
            dim.AddEdge("city", "village", 0.5f);

            var field = dim.AddField("f", TradeProfile());
            field.SetLogAmp("city", "grain", 3f);

            // Initially village has no signal
            Assert.AreEqual(0f, field.GetLogAmp("village", "grain"), 1e-9f);

            // After a few ticks, signal must arrive
            for (int tick = 0; tick < 10; tick++)
                Propagator.Step(dim, field, 0.5f);

            Assert.Greater(field.GetLogAmp("village", "grain"), 0f,
                "Signal must propagate from city to village over multiple ticks");
        }

        // ─── No cross-field contamination ────────────────────────────────────

        [Test]
        public void Scenario_MultiField_NoCrossContamination()
        {
            var dim = new Dimension();
            dim.AddNode("n"); dim.AddNode("m");
            dim.AddEdge("n", "m", 0f);

            var field1 = dim.AddField("f1", TradeProfile("f1"));
            var field2 = dim.AddField("f2", TradeProfile("f2"));

            field1.SetLogAmp("n", "signal", 5f);

            for (int tick = 0; tick < 20; tick++)
                Propagator.Step(dim, field1, 0.1f);
            // Only propagate field1 — field2 must remain zero

            Assert.AreEqual(0f, field2.GetLogAmp("n", "signal"), 1e-9f,
                "Signal in field1 must not contaminate field2");
            Assert.AreEqual(0f, field2.GetLogAmp("m", "signal"), 1e-9f,
                "Signal in field1 must not contaminate field2");
        }

        // ─── Economy scenario: repeated buying raises price ───────────────────

        [Test]
        public void Scenario_RepeatedBuys_PriceRisesMonotonically_Over10Ticks()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            var eco = new EconomySystem(dim, EcoProfile());

            float prevPrice = eco.SamplePrice("market", "wheat", 10f);

            for (int i = 0; i < 10; i++)
            {
                eco.InjectTrade("market", "wheat", 20f);

                // Propagate both fields one tick
                Propagator.Step(dim, eco.Availability, 0.1f);
                Propagator.Step(dim, eco.PricePressure, 0.1f);

                float currentPrice = eco.SamplePrice("market", "wheat", 10f);
                Assert.GreaterOrEqual(currentPrice, prevPrice,
                    $"Price must not decrease after buy #{i + 1}");
                prevPrice = currentPrice;
            }
        }

        [Test]
        public void Scenario_RepeatedBuys_PriceIsAlwaysPositive()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            var eco = new EconomySystem(dim, EcoProfile());

            for (int i = 0; i < 50; i++)
            {
                eco.InjectTrade("market", "fuel", 10f);
                Propagator.Step(dim, eco.Availability, 0.05f);
                Propagator.Step(dim, eco.PricePressure, 0.05f);

                float price = eco.SamplePrice("market", "fuel", 5f);
                Assert.Greater(price, 0f, $"Price must always be positive at trade #{i + 1}");
                Assert.IsFalse(float.IsNaN(price), $"Price must never be NaN at trade #{i + 1}");
                Assert.IsFalse(float.IsInfinity(price),
                    $"Price must never be Infinity at trade #{i + 1}");
            }
        }

        // ─── Network-wide propagation ─────────────────────────────────────────

        [Test]
        public void Scenario_SignalFromOneEndReachesOtherEndAfterEnoughTicks()
        {
            // A long chain — signal must traverse the whole chain over enough ticks
            int chainLength = 10;
            var dim = new Dimension();
            for (int i = 0; i < chainLength; i++) dim.AddNode($"n{i}");
            for (int i = 0; i < chainLength - 1; i++) dim.AddEdge($"n{i}", $"n{i + 1}", 0f);

            var field = dim.AddField("f", new FieldProfile("f")
            {
                PropagationRate = 1f,
                DecayRate = 0f,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            });
            field.SetLogAmp("n0", "wave", 5f);

            // Need at least chainLength-1 steps for signal to reach the end
            for (int tick = 0; tick < chainLength; tick++)
                Propagator.Step(dim, field, 1f);

            Assert.Greater(field.GetLogAmp($"n{chainLength - 1}", "wave"), 0f,
                $"Signal must reach end of chain after {chainLength} steps");
        }

        // ─── Equilibrium scenario ─────────────────────────────────────────────

        [Test]
        public void Scenario_SymmetricNetwork_ReachesNearSymmetricEquilibrium()
        {
            // Two nodes feeding each other symmetrically should converge to equal amplitudes
            var dim = new Dimension();
            dim.AddNode("a"); dim.AddNode("b");
            dim.AddEdge("a", "b", 0f);
            dim.AddEdge("b", "a", 0f);

            var profile = new FieldProfile("sym")
            {
                PropagationRate = 0.5f,
                DecayRate = 0.5f,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            };
            var field = dim.AddField("f", profile);
            field.SetLogAmp("a", "ch", 4f);
            field.SetLogAmp("b", "ch", 4f);

            for (int tick = 0; tick < 200; tick++)
                Propagator.Step(dim, field, 0.1f);

            float aAmp = field.GetLogAmp("a", "ch");
            float bAmp = field.GetLogAmp("b", "ch");

            Assert.AreEqual(aAmp, bAmp, 0.01f,
                "Symmetric network with equal initial conditions must converge symmetrically");
        }

        // ─── Tag-filtered scenario (trade lanes) ──────────────────────────────

        [Test]
        public void Scenario_TradeRoute_SeaLane_OnlyPropagatesAlongTaggedEdges()
        {
            // Two markets connected by sea AND land. Sea-only propagation must skip land route.
            var dim = new Dimension();
            dim.AddNode("port-a"); dim.AddNode("port-b"); dim.AddNode("inland");

            dim.AddEdge("port-a", "port-b", 0f, "sea");
            dim.AddEdge("port-a", "inland", 0f, "land");

            var field = dim.AddField("f", TradeProfile());
            field.SetLogAmp("port-a", "cargo", 5f);

            Propagator.Step(dim, field, 1f, requiredEdgeTag: "sea");

            Assert.Greater(field.GetLogAmp("port-b", "cargo"), 0f,
                "Sea lane must carry cargo to port-b");
            Assert.AreEqual(0f, field.GetLogAmp("inland", "cargo"), 1e-9f,
                "Land route must be blocked by sea-only filter");
        }
    }
}
