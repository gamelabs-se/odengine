using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;
using Odengine.War;

namespace Odengine.Tests.War
{
    /// <summary>
    /// Tests for WarSystem.
    ///
    /// Tiers:
    ///   Core       — field math, state machine transitions, determinism
    ///   Domain     — single-system API correctness
    ///   Integration — exposure propagation, occupation lifecycle
    ///   Scenario   — multi-tick invariant checks
    /// </summary>
    [TestFixture]
    public class War_WarSystemTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static FieldProfile MakeExposureProfile(
            float propagationRate = 0f,
            float edgeResistanceScale = 1f,
            float decayRate = 0f)
        {
            return new FieldProfile("war.exposure")
            {
                PropagationRate = propagationRate,
                EdgeResistanceScale = edgeResistanceScale,
                DecayRate = decayRate,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            };
        }

        /// <summary>Build a Dimension + WarSystem with no graph edges.</summary>
        private static (Dimension dim, WarSystem war) MakeSingle(string nodeId)
        {
            var dim = new Dimension();
            dim.AddNode(nodeId);
            var war = new WarSystem(dim, MakeExposureProfile());
            return (dim, war);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: field storage
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Peace_ExposureIsNeutral()
        {
            var (_, war) = MakeSingle("earth");

            Assert.That(war.GetExposureLogAmp("earth"), Is.EqualTo(0f));
            Assert.That(war.GetExposureMultiplier("earth"), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void ActiveWar_ExposureGrows_EachTick()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");

            float prev = war.GetExposureLogAmp("earth");
            for (int i = 0; i < 5; i++)
            {
                war.Tick(1f);
                float curr = war.GetExposureLogAmp("earth");
                Assert.That(curr, Is.GreaterThan(prev), $"Tick {i + 1}: exposure should grow");
                prev = curr;
            }
        }

        [Test]
        public void ActiveWar_ExposureGrows_Proportional_To_Dt()
        {
            var (_, warA) = MakeSingle("earth");
            var (_, warB) = MakeSingle("earth");

            warA.DeclareWar("earth");
            warB.DeclareWar("earth");

            warA.Tick(1f);
            warB.Tick(0.5f);
            warB.Tick(0.5f);

            Assert.That(
                warA.GetExposureLogAmp("earth"),
                Is.EqualTo(warB.GetExposureLogAmp("earth")).Within(1e-5f),
                "Two half-ticks should equal one full tick");
        }

        [Test]
        public void Peace_AmbientDecay_ReducesExposure()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            war.Tick(10f); // build up exposure
            war.DeclareCeasefire("earth");

            // Run until cooling is done so node is in ambient state
            for (int i = 0; i < 100; i++) war.Tick(1f);

            // Manually add some exposure to put node in ambient state
            // (ceasefire should have already brought it to zero, then inject)
            var (dim2, war2) = MakeSingle("earth");
            war2.Exposure.AddLogAmp("earth", "x", 1f);

            float before = war2.GetExposureLogAmp("earth");
            war2.Tick(1f); // ambient decay applies
            float after = war2.GetExposureLogAmp("earth");

            Assert.That(after, Is.LessThan(before), "Ambient decay should reduce exposure");
        }

        [Test]
        public void Ceasefire_ExposureDecays_FasterThanAmbient()
        {
            // Inject same initial logAmp into two systems, one cooling, one ambient
            var (_, warCeasefire) = MakeSingle("earth");
            var (_, warAmbient) = MakeSingle("earth");

            warCeasefire.Exposure.AddLogAmp("earth", "x", 2f);
            warAmbient.Exposure.AddLogAmp("earth", "x", 2f);

            // Mark one as cooling (ceasefire active)
            warCeasefire.DeclareWar("earth");
            warCeasefire.Tick(0f); // no-op tick to ensure war is registered
            warCeasefire.DeclareCeasefire("earth");

            warCeasefire.Tick(1f);
            warAmbient.Tick(1f); // ambient only

            float ceasefireRemaining = warCeasefire.GetExposureLogAmp("earth");
            float ambientRemaining = warAmbient.GetExposureLogAmp("earth");

            Assert.That(ceasefireRemaining, Is.LessThan(ambientRemaining),
                "Ceasefire decay should be faster than ambient decay");
        }

        [Test]
        public void Ceasefire_Eventually_ReachesZero()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            for (int i = 0; i < 10; i++) war.Tick(1f);

            war.DeclareCeasefire("earth");
            for (int i = 0; i < 200; i++) war.Tick(1f);

            Assert.That(war.GetExposureLogAmp("earth"), Is.EqualTo(0f).Within(1e-4f),
                "Ceasefire should decay exposure to zero");
        }

        [Test]
        public void Ceasefire_ExposureDecays_Monotonically()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            for (int i = 0; i < 5; i++) war.Tick(1f);
            war.DeclareCeasefire("earth");

            float prev = war.GetExposureLogAmp("earth");
            for (int i = 0; i < 30; i++)
            {
                war.Tick(1f);
                float curr = war.GetExposureLogAmp("earth");
                Assert.That(curr, Is.LessThanOrEqualTo(prev + 1e-6f),
                    $"Exposure should not increase during ceasefire (tick {i + 1})");
                prev = curr;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: state machine
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void IsAtWar_ReturnsTrue_AfterDeclareWar()
        {
            var (_, war) = MakeSingle("earth");
            Assert.That(war.IsAtWar("earth"), Is.False);
            war.DeclareWar("earth");
            Assert.That(war.IsAtWar("earth"), Is.True);
        }

        [Test]
        public void IsCooling_ReturnsFalse_WhenAtWar()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            Assert.That(war.IsCooling("earth"), Is.False);
        }

        [Test]
        public void DeclareCeasefire_MovesNode_ToColing()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            war.DeclareCeasefire("earth");

            Assert.That(war.IsAtWar("earth"), Is.False);
            Assert.That(war.IsCooling("earth"), Is.True);
        }

        [Test]
        public void Ceasefire_Without_ActiveWar_IsNoOp()
        {
            var (_, war) = MakeSingle("earth");
            // Never declared war
            war.DeclareCeasefire("earth");
            Assert.That(war.IsCooling("earth"), Is.False);
        }

        [Test]
        public void DeclareWar_Is_Idempotent()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            war.Tick(1f);
            float after1 = war.GetExposureLogAmp("earth");

            war.DeclareWar("earth"); // again
            war.Tick(1f);
            float after2 = war.GetExposureLogAmp("earth");

            // Growth should be the same both ticks (0.05 * 1 each time)
            float delta1 = after1;
            float delta2 = after2 - after1;
            Assert.That(delta1, Is.EqualTo(delta2).Within(1e-5f),
                "Declaring war twice should not double-count growth");
        }

        [Test]
        public void Ceasefire_Clears_CoolingState_WhenExposureReachesZero()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            war.Tick(2f);
            war.DeclareCeasefire("earth");

            // Run until fully decayed
            for (int i = 0; i < 200; i++) war.Tick(1f);

            Assert.That(war.IsCooling("earth"), Is.False,
                "Cooling state should be cleared once exposure hits zero");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: multiple nodes are independent
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void WarOnOneNode_DoesNotAffect_PeacefulNode()
        {
            var dim = new Dimension();
            dim.AddNode("earth");
            dim.AddNode("mars");
            var war = new WarSystem(dim, MakeExposureProfile(propagationRate: 0f));

            war.DeclareWar("earth");
            for (int i = 0; i < 10; i++) war.Tick(1f);

            Assert.That(war.GetExposureLogAmp("earth"), Is.GreaterThan(0f));
            Assert.That(war.GetExposureLogAmp("mars"), Is.EqualTo(0f),
                "Peaceful node should be unaffected when propagation is disabled");
        }

        [Test]
        public void MultipleNodes_IndependentStateDecay()
        {
            var dim = new Dimension();
            dim.AddNode("a");
            dim.AddNode("b");
            var war = new WarSystem(dim, MakeExposureProfile());

            war.DeclareWar("a");
            war.DeclareWar("b");
            for (int i = 0; i < 5; i++) war.Tick(1f);

            war.DeclareCeasefire("a");
            // b stays at war

            for (int i = 0; i < 10; i++) war.Tick(1f);

            Assert.That(war.GetExposureLogAmp("a"), Is.LessThan(war.GetExposureLogAmp("b")),
                "'a' (ceasefire) should have less exposure than 'b' (still at war)");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Domain: occupation lifecycle
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Occupation_ProgressBegins_AfterDeclare()
        {
            var (_, war) = MakeSingle("earth");
            war.SetNodeStability("earth", 0f); // no resistance
            war.DeclareOccupation("earth", "attacker_a");

            war.Tick(1f);

            Assert.That(war.GetOccupationProgress("earth"), Is.GreaterThan(0f),
                "Progress should begin after one tick");
            Assert.That(war.GetOccupationAttacker("earth"), Is.EqualTo("attacker_a"));
        }

        [Test]
        public void Occupation_NoProgress_WithoutDeclare()
        {
            var (_, war) = MakeSingle("earth");
            war.Tick(1f);
            Assert.That(war.GetOccupationProgress("earth"), Is.EqualTo(0f));
            Assert.That(war.GetOccupationAttacker("earth"), Is.Null);
        }

        [Test]
        public void Occupation_Completes_WithCallback()
        {
            var (_, war) = MakeSingle("earth");
            war.SetNodeStability("earth", 0f); // no resistance → fastest

            string completedNode = null;
            string completedAttacker = null;
            war.OnOccupationComplete = (n, a) => { completedNode = n; completedAttacker = a; };

            war.DeclareOccupation("earth", "empire_b");

            // OccupationBaseRate = 0.1 → 10 ticks at dt=1 to complete
            for (int i = 0; i < 15; i++) war.Tick(1f);

            Assert.That(completedNode, Is.EqualTo("earth"), "Callback should fire with correct nodeId");
            Assert.That(completedAttacker, Is.EqualTo("empire_b"), "Callback should fire with correct attackerId");
        }

        [Test]
        public void Occupation_RemovedFromTracking_AfterComplete()
        {
            var (_, war) = MakeSingle("earth");
            war.SetNodeStability("earth", 0f);
            war.DeclareOccupation("earth", "empire_b");

            for (int i = 0; i < 15; i++) war.Tick(1f);

            Assert.That(war.GetOccupationProgress("earth"), Is.EqualTo(0f),
                "Progress should be cleared after occupation completes");
            Assert.That(war.GetOccupationAttacker("earth"), Is.Null,
                "Attacker should be cleared after occupation completes");
        }

        [Test]
        public void Occupation_IsNotInstant()
        {
            var (_, war) = MakeSingle("earth");
            war.SetNodeStability("earth", 0f);
            war.DeclareOccupation("earth", "attacker_a");
            war.Tick(1f);

            // After 1 tick progress should be ~0.1, not >= 1
            Assert.That(war.GetOccupationProgress("earth"), Is.LessThan(1f),
                "Occupation should take multiple ticks");
        }

        [Test]
        public void Occupation_HighStability_SlowsProgress()
        {
            var (_, warLow) = MakeSingle("earth");
            var (_, warHigh) = MakeSingle("earth");

            warLow.SetNodeStability("earth", 0f);
            warHigh.SetNodeStability("earth", 1f);

            warLow.DeclareOccupation("earth", "attacker");
            warHigh.DeclareOccupation("earth", "attacker");

            warLow.Tick(1f);
            warHigh.Tick(1f);

            Assert.That(warLow.GetOccupationProgress("earth"),
                Is.GreaterThan(warHigh.GetOccupationProgress("earth")),
                "High stability should slow occupation progress");
        }

        [Test]
        public void CancelOccupation_RemovesAttempt()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareOccupation("earth", "attacker_a");
            war.CancelOccupation("earth");

            war.Tick(5f);
            Assert.That(war.GetOccupationProgress("earth"), Is.EqualTo(0f));
            Assert.That(war.GetOccupationAttacker("earth"), Is.Null);
        }

        [Test]
        public void DeclareOccupation_Replaces_ExistingAttempt()
        {
            var (_, war) = MakeSingle("earth");
            war.SetNodeStability("earth", 0f);
            war.DeclareOccupation("earth", "attacker_a");
            war.Tick(3f);

            // Attacker B takes over the attempt — progress resets
            war.DeclareOccupation("earth", "attacker_b");
            Assert.That(war.GetOccupationProgress("earth"), Is.EqualTo(0f));
            Assert.That(war.GetOccupationAttacker("earth"), Is.EqualTo("attacker_b"));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Integration: propagation across edges
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Exposure_Propagates_ToNeighbor()
        {
            var dim = new Dimension();
            dim.AddNode("front");
            dim.AddNode("rear");
            dim.AddEdge("front", "rear", resistance: 0f);

            var war = new WarSystem(dim, MakeExposureProfile(propagationRate: 1f));

            war.DeclareWar("front");
            for (int i = 0; i < 5; i++) war.Tick(1f);

            Assert.That(war.GetExposureLogAmp("rear"), Is.GreaterThan(0f),
                "War exposure should propagate to neighbor node");
        }

        [Test]
        public void Exposure_Propagation_AttenuatedByResistance()
        {
            var dim = new Dimension();
            dim.AddNode("front");
            dim.AddNode("near");
            dim.AddNode("far");
            dim.AddEdge("front", "near", resistance: 0f);
            dim.AddEdge("front", "far", resistance: 5f);

            var war = new WarSystem(dim, MakeExposureProfile(propagationRate: 1f));
            war.DeclareWar("front");
            for (int i = 0; i < 5; i++) war.Tick(1f);

            Assert.That(war.GetExposureLogAmp("near"),
                Is.GreaterThan(war.GetExposureLogAmp("far")),
                "Lower resistance edge should receive more exposure");
        }

        [Test]
        public void Propagation_Respects_EdgeTag_Filter()
        {
            var dim = new Dimension();
            dim.AddNode("front");
            dim.AddNode("land_node");
            dim.AddNode("sea_node");
            dim.AddEdge("front", "land_node", resistance: 0f, "land");
            dim.AddEdge("front", "sea_node", resistance: 0f, "sea");

            // Create a WarSystem but manually call Propagator with a tag filter
            // to verify the edge tag mechanism works correctly
            var profile = MakeExposureProfile(propagationRate: 0f); // disable auto-prop
            var war = new WarSystem(dim, profile);
            war.Exposure.AddLogAmp("front", "x", 2f);

            // Manual step with tag filter
            var seaProfile = MakeExposureProfile(propagationRate: 1f);
            var seaField = dim.AddField("war.sea_test", seaProfile);
            seaField.AddLogAmp("front", "x", 2f);

            Propagator.Step(dim, seaField, 1f, requiredEdgeTag: "sea");

            Assert.That(seaField.GetLogAmp("sea_node", "x"), Is.GreaterThan(0f),
                "Sea-tagged propagation should reach sea_node");
            Assert.That(seaField.GetLogAmp("land_node", "x"), Is.EqualTo(0f),
                "Sea-tagged propagation should not reach land_node");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Integration: exposure vs economy coupling (manual impulse)
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void WarExposure_CanDriveEconomyImpulse()
        {
            // Demonstrate the coupling pattern: game layer reads war exposure
            // and injects an impulse into the economy field.
            var dim = new Dimension();
            dim.AddNode("earth");

            var warProfile = MakeExposureProfile();
            var war = new WarSystem(dim, warProfile);

            var ecoProfile = new FieldProfile("economy.availability")
            {
                PropagationRate = 0f,
                DecayRate = 0f,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            };
            var availField = dim.AddField("economy.availability", ecoProfile);

            war.DeclareWar("earth");
            for (int i = 0; i < 10; i++)
            {
                war.Tick(1f);
                // Game-layer coupling: war reduces supply
                float exposure = war.GetExposureLogAmp("earth");
                availField.AddLogAmp("earth", "food", -exposure * 0.1f);
            }

            Assert.That(availField.GetLogAmp("earth", "food"), Is.LessThan(0f),
                "War exposure should drive negative impulses into economy availability");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Determinism
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameInputs_ProduceSameExposure()
        {
            float Run()
            {
                var dim = new Dimension();
                dim.AddNode("earth");
                dim.AddNode("mars");
                dim.AddEdge("earth", "mars", resistance: 1f);
                var war = new WarSystem(dim, MakeExposureProfile(propagationRate: 0.5f));
                war.DeclareWar("earth");
                for (int i = 0; i < 20; i++) war.Tick(1f);
                war.DeclareCeasefire("earth");
                for (int i = 0; i < 5; i++) war.Tick(1f);
                return war.GetExposureLogAmp("mars");
            }

            float a = Run();
            float b = Run();
            Assert.That(a, Is.EqualTo(b).Within(1e-7f),
                "Identical runs must produce bit-identical exposure values");
        }

        [Test]
        public void Determinism_NodeInsertionOrder_DoesNotAffectResult()
        {
            float RunOrder(string first, string second)
            {
                var dim = new Dimension();
                dim.AddNode(first);
                dim.AddNode(second);
                dim.AddEdge("a", "b", resistance: 0f);
                var war = new WarSystem(dim, MakeExposureProfile(propagationRate: 1f));
                war.DeclareWar("a");
                war.DeclareWar("b");
                for (int i = 0; i < 10; i++) war.Tick(1f);
                return war.GetExposureLogAmp("a") + war.GetExposureLogAmp("b");
            }

            float ab = RunOrder("a", "b");
            float ba = RunOrder("b", "a");
            Assert.That(ab, Is.EqualTo(ba).Within(1e-5f),
                "Node insertion order must not affect simulation result");
        }

        [Test]
        public void Determinism_OccupationOrder_IsStable()
        {
            int callCount = 0;

            void Run()
            {
                var dim = new Dimension();
                dim.AddNode("earth");
                dim.AddNode("mars");
                var war = new WarSystem(dim, MakeExposureProfile());
                war.SetNodeStability("earth", 0f);
                war.SetNodeStability("mars", 0f);
                war.DeclareOccupation("earth", "empire_a");
                war.DeclareOccupation("mars", "empire_a");
                war.OnOccupationComplete = (n, a) => callCount++;
                for (int i = 0; i < 15; i++) war.Tick(1f);
            }

            Run();
            Assert.That(callCount, Is.EqualTo(2), "Both occupations should complete");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Scenario: 100-tick stability invariants
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Scenario_100Ticks_NoNaN_NoInfinity()
        {
            var dim = new Dimension();
            var nodes = new[] { "alpha", "beta", "gamma", "delta" };
            foreach (var n in nodes) dim.AddNode(n);
            dim.AddEdge("alpha", "beta", resistance: 0.5f);
            dim.AddEdge("beta", "gamma", resistance: 1f);
            dim.AddEdge("gamma", "delta", resistance: 2f);
            dim.AddEdge("delta", "alpha", resistance: 0.5f);

            var war = new WarSystem(dim, MakeExposureProfile(propagationRate: 0.3f, decayRate: 0.01f));
            war.DeclareWar("alpha");
            war.DeclareWar("gamma");
            war.SetNodeStability("beta", 0.5f);
            war.DeclareOccupation("beta", "attacker");

            int occupations = 0;
            war.OnOccupationComplete = (n, a) => occupations++;

            for (int tick = 0; tick < 100; tick++)
            {
                if (tick == 30) war.DeclareCeasefire("alpha");
                if (tick == 50) war.DeclareWar("delta");
                war.Tick(1f);

                foreach (var n in nodes)
                {
                    float logAmp = war.GetExposureLogAmp(n);
                    Assert.That(float.IsNaN(logAmp), Is.False, $"NaN at tick {tick}, node {n}");
                    Assert.That(float.IsInfinity(logAmp), Is.False, $"Infinity at tick {tick}, node {n}");
                }
            }
        }

        [Test]
        public void Scenario_Exposure_ClampedWithinProfile()
        {
            var profile = new FieldProfile("war.exposure")
            {
                PropagationRate = 0f,
                DecayRate = 0f,
                MinLogAmpClamp = -5f,
                MaxLogAmpClamp = 5f
            };
            var dim = new Dimension();
            dim.AddNode("earth");
            var war = new WarSystem(dim, profile);
            war.DeclareWar("earth");

            for (int i = 0; i < 1000; i++) war.Tick(1f);

            Assert.That(war.GetExposureLogAmp("earth"), Is.LessThanOrEqualTo(5f + 1e-5f),
                "Exposure logAmp should not exceed MaxLogAmpClamp");
        }

        [Test]
        public void Scenario_Ceasefire_After_LongWar_DecaysToZero()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            for (int i = 0; i < 200; i++) war.Tick(1f);

            float peak = war.GetExposureLogAmp("earth");
            Assert.That(peak, Is.GreaterThan(0f));

            war.DeclareCeasefire("earth");
            for (int i = 0; i < 1000; i++) war.Tick(1f);

            Assert.That(war.GetExposureLogAmp("earth"), Is.EqualTo(0f).Within(1e-4f),
                "Even after a long war, ceasefire should eventually bring exposure to zero");
            Assert.That(war.IsCooling("earth"), Is.False,
                "Cooling state should be cleared");
        }

        [Test]
        public void Scenario_MultiWave_WarCeasefireWar_CorrectBehaviour()
        {
            var (_, war) = MakeSingle("earth");

            // Wave 1
            war.DeclareWar("earth");
            for (int i = 0; i < 10; i++) war.Tick(1f);
            float peakWave1 = war.GetExposureLogAmp("earth");

            // Ceasefire
            war.DeclareCeasefire("earth");
            for (int i = 0; i < 50; i++) war.Tick(1f);
            float afterCeasefire = war.GetExposureLogAmp("earth");

            // Wave 2
            war.DeclareWar("earth");
            for (int i = 0; i < 10; i++) war.Tick(1f);
            float peakWave2 = war.GetExposureLogAmp("earth");

            Assert.That(peakWave1, Is.GreaterThan(0f));
            Assert.That(afterCeasefire, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(peakWave2, Is.GreaterThan(0f),
                "Second war wave should build exposure again");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Edge: invalid inputs
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void DeclareWar_NullNodeId_Throws()
        {
            var (_, war) = MakeSingle("earth");
            Assert.Throws<ArgumentException>(() => war.DeclareWar(null));
        }

        [Test]
        public void DeclareOccupation_EmptyAttacker_Throws()
        {
            var (_, war) = MakeSingle("earth");
            Assert.Throws<ArgumentException>(() => war.DeclareOccupation("earth", ""));
        }

        [Test]
        public void Tick_ZeroDt_IsNoOp()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            war.Tick(0f); // should not throw or advance
            Assert.That(war.GetExposureLogAmp("earth"), Is.EqualTo(0f));
        }

        [Test]
        public void Tick_NegativeDt_IsNoOp()
        {
            var (_, war) = MakeSingle("earth");
            war.DeclareWar("earth");
            war.Tick(-5f);
            Assert.That(war.GetExposureLogAmp("earth"), Is.EqualTo(0f));
        }

        [Test]
        public void GetExposure_UnknownNode_ReturnsNeutral()
        {
            var (_, war) = MakeSingle("earth");
            Assert.That(war.GetExposureLogAmp("nonexistent"), Is.EqualTo(0f));
            Assert.That(war.GetExposureMultiplier("nonexistent"), Is.EqualTo(1f).Within(1e-5f));
        }
    }
}
