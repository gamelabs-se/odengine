using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Faction;
using Odengine.War;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Tests.Faction
{
    /// <summary>
    /// Tests for FactionSystem and FactionState.
    ///
    /// Tiers:
    ///   Core       — aggregate math, stability smoothing, exhaustion dynamics
    ///   Domain     — single-system API, control management
    ///   Integration — war-to-faction bridge, multi-faction interaction
    ///   Scenario   — multi-tick invariant checks, callback correctness
    ///   Determinism — sorted iteration, dt-invariance
    /// </summary>
    [TestFixture]
    public class Faction_FactionSystemTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static FactionSystem MakeSystem() => new FactionSystem();

        /// <summary>
        /// Build a system with two factions, each controlling one node,
        /// with explicit stability and war exposure set.
        /// </summary>
        private static FactionSystem MakeTwoFactionWorld(
            float alphaStability = 0.8f, float alphaWar = 0f,
            float betaStability  = 0.6f, float betaWar  = 0f)
        {
            var fs = new FactionSystem();
            fs.SetNodeController("node_a", "faction_alpha");
            fs.SetNodeController("node_b", "faction_beta");
            fs.SetNodeStability("node_a",  alphaStability);
            fs.SetNodeStability("node_b",  betaStability);
            fs.SetNodeWarExposure("node_a", alphaWar);
            fs.SetNodeWarExposure("node_b", betaWar);
            return fs;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: initial state
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void NewFaction_PoliticalStability_IsOne()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("empire");
            Assert.That(fs.GetFaction("empire").PoliticalStability, Is.EqualTo(1f));
        }

        [Test]
        public void NewFaction_WarExhaustion_IsZero()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("empire");
            Assert.That(fs.GetFaction("empire").WarExhaustion, Is.EqualTo(0f));
        }

        [Test]
        public void NewFaction_IsCollapsing_IsFalse()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("empire");
            Assert.That(fs.GetFaction("empire").IsCollapsing, Is.False);
        }

        [Test]
        public void NewFaction_ControlledNodeCount_IsZero()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("empire");
            Assert.That(fs.GetFaction("empire").ControlledNodeCount, Is.EqualTo(0));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: aggregate computation
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Aggregate_SingleNode_ControlledNodeCount_IsOne()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").ControlledNodeCount, Is.EqualTo(1));
        }

        [Test]
        public void Aggregate_SingleNode_AverageStability_MatchesNodeStability()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0.75f);
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").AverageStability, Is.EqualTo(0.75f).Within(1e-5f));
        }

        [Test]
        public void Aggregate_TwoNodes_AverageStability_IsMean()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeController("mars",  "empire");
            fs.SetNodeStability("earth", 0.8f);
            fs.SetNodeStability("mars",  0.4f);
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").AverageStability, Is.EqualTo(0.6f).Within(1e-5f));
        }

        [Test]
        public void Aggregate_TwoNodes_AverageWarExposure_IsMean()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeController("mars",  "empire");
            fs.SetNodeWarExposure("earth", 0.4f);
            fs.SetNodeWarExposure("mars",  0.8f);
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").AverageWarExposure, Is.EqualTo(0.6f).Within(1e-5f));
        }

        [Test]
        public void Aggregate_NodeNotControlled_ExcludedFromAverages()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeController("venus", "rebels"); // different faction
            fs.SetNodeStability("earth", 0.9f);
            fs.SetNodeStability("venus", 0.1f);
            fs.Tick(1f);

            // empire's average should only reflect earth
            Assert.That(fs.GetFaction("empire").AverageStability, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(fs.GetFaction("rebels").AverageStability, Is.EqualTo(0.1f).Within(1e-5f));
        }

        [Test]
        public void Aggregate_NodeWithNoStabilitySet_DefaultsToOne()
        {
            // Stability defaults to 1.0 (fully stable baseline) when not explicitly set
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            // No SetNodeStability call
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").AverageStability, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Aggregate_NodeWithNoWarExposureSet_DefaultsToZero()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            // No SetNodeWarExposure call
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").AverageWarExposure, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Aggregate_NoControlledNodes_AverageStabilityMirrorsPoliticalStability()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("empire");
            // Manually set political stability to a known value by ticking toward a controlled node
            // then removing it
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0.5f);
            for (int i = 0; i < 50; i++) fs.Tick(1f);
            float polStability = fs.GetFaction("empire").PoliticalStability;

            // Remove control
            fs.ClearNodeController("earth");
            fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").AverageStability,
                Is.EqualTo(polStability).Within(1e-4f),
                "With no controlled nodes, AverageStability should mirror PoliticalStability");
        }

        [Test]
        public void Aggregate_ControlledNodeCount_ResetEachTick()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeController("mars",  "empire");
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").ControlledNodeCount, Is.EqualTo(2));

            fs.ClearNodeController("mars");
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").ControlledNodeCount, Is.EqualTo(1),
                "ControlledNodeCount should reflect current tick, not accumulate");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: political stability smoothing
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void PoliticalStability_SmoothsToward_AverageStability()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0.2f); // push toward low stability

            // Start at 1.0 (default), run many ticks
            for (int i = 0; i < 200; i++) fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").PoliticalStability,
                Is.LessThan(0.5f),
                "PoliticalStability should move toward AverageStability (0.2)");
        }

        [Test]
        public void PoliticalStability_EventuallyConverges_ToAverageStability()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0.3f);

            for (int i = 0; i < 1000; i++) fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").PoliticalStability,
                Is.EqualTo(0.3f).Within(0.01f),
                "PoliticalStability should converge to AverageStability over time");
        }

        [Test]
        public void PoliticalStability_IsClampedAt_Zero()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0f);
            for (int i = 0; i < 10000; i++) fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").PoliticalStability, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void PoliticalStability_IsClampedAt_One()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 1f);
            for (int i = 0; i < 100; i++) fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").PoliticalStability, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void PoliticalStability_DeltaInvariant_TwoHalfTicksEqualOneFull()
        {
            // Full tick system
            var fsFull = MakeSystem();
            fsFull.SetNodeController("earth", "empire");
            fsFull.SetNodeStability("earth", 0.2f);
            fsFull.Tick(1f);
            float full = fsFull.GetFaction("empire").PoliticalStability;

            // Two half-tick system
            var fsHalf = MakeSystem();
            fsHalf.SetNodeController("earth", "empire");
            fsHalf.SetNodeStability("earth", 0.2f);
            fsHalf.Tick(0.5f);
            fsHalf.Tick(0.5f);
            float half = fsHalf.GetFaction("empire").PoliticalStability;

            Assert.That(full, Is.EqualTo(half).Within(1e-5f),
                "Two half-ticks should be identical to one full tick (exponential smoothing is dt-correct)");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: war exhaustion
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void WarExhaustion_Grows_WhenWarExposureAboveThreshold()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeWarExposure("earth", 1f); // well above 0.1 threshold

            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").WarExhaustion, Is.GreaterThan(0f));
        }

        [Test]
        public void WarExhaustion_DoesNotGrow_WhenWarExposureBelowThreshold()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeWarExposure("earth", 0.05f); // below 0.1 threshold

            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").WarExhaustion, Is.EqualTo(0f));
        }

        [Test]
        public void WarExhaustion_Decays_WhenBelowThreshold()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("empire");
            // Manually inject exhaustion via high-war ticks
            fs.SetNodeController("earth", "empire");
            fs.SetNodeWarExposure("earth", 2f);
            for (int i = 0; i < 20; i++) fs.Tick(1f);
            float peakExhaustion = fs.GetFaction("empire").WarExhaustion;

            // Switch to peace
            fs.SetNodeWarExposure("earth", 0f);
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").WarExhaustion,
                Is.LessThan(peakExhaustion),
                "Exhaustion should decay when war exposure drops below threshold");
        }

        [Test]
        public void WarExhaustion_EventuallyReachesZero_InPeace()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeWarExposure("earth", 2f);
            for (int i = 0; i < 50; i++) fs.Tick(1f);

            fs.SetNodeWarExposure("earth", 0f);
            for (int i = 0; i < 1000; i++) fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").WarExhaustion, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void WarExhaustion_ClampedAt_One()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeWarExposure("earth", 20f); // max possible logAmp
            for (int i = 0; i < 10000; i++) fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").WarExhaustion, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void WarExhaustion_NeverNegative()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeWarExposure("earth", 0f);
            for (int i = 0; i < 10000; i++) fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").WarExhaustion, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void WarExhaustion_DeltaInvariant()
        {
            float RunPath(float[] dts)
            {
                var fs = new FactionSystem();
                fs.SetNodeController("earth", "empire");
                fs.SetNodeWarExposure("earth", 1f);
                foreach (float dt in dts) fs.Tick(dt);
                return fs.GetFaction("empire").WarExhaustion;
            }

            // 10 ticks of 1.0 vs 1 tick of 10.0
            float a = RunPath(new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f });
            float b = RunPath(new[] { 10f });
            Assert.That(a, Is.EqualTo(b).Within(1e-5f),
                "WarExhaustion should be invariant to tick splitting (linear accumulation)");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: IsCollapsing
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void IsCollapsing_True_WhenStabilityBelowCrisisThreshold()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0f);

            // Run until stability drops below 0.3
            for (int i = 0; i < 1000; i++) fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").IsCollapsing, Is.True);
        }

        [Test]
        public void IsCollapsing_False_WhenStabilityAboveCrisisThreshold()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 1f);
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").IsCollapsing, Is.False);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Domain: faction API
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void RegisterFaction_IsIdempotent()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("empire");
            fs.RegisterFaction("empire"); // second call
            Assert.That(fs.GetFactionIdsSorted().Count, Is.EqualTo(1));
        }

        [Test]
        public void GetFaction_ReturnsNull_ForUnknownFaction()
        {
            var fs = MakeSystem();
            Assert.That(fs.GetFaction("no_such_faction"), Is.Null);
        }

        [Test]
        public void HasFaction_ReturnsFalse_BeforeRegister()
        {
            var fs = MakeSystem();
            Assert.That(fs.HasFaction("empire"), Is.False);
        }

        [Test]
        public void HasFaction_ReturnsTrue_AfterRegister()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("empire");
            Assert.That(fs.HasFaction("empire"), Is.True);
        }

        [Test]
        public void GetFactionIdsSorted_ReturnsDeterministicOrder()
        {
            var fs = MakeSystem();
            fs.RegisterFaction("zebra");
            fs.RegisterFaction("alpha");
            fs.RegisterFaction("mango");

            var ids = fs.GetFactionIdsSorted();
            Assert.That(ids[0], Is.EqualTo("alpha"));
            Assert.That(ids[1], Is.EqualTo("mango"));
            Assert.That(ids[2], Is.EqualTo("zebra"));
        }

        [Test]
        public void RegisterFaction_ThrowsOnNullId()
        {
            var fs = MakeSystem();
            Assert.Throws<ArgumentException>(() => fs.RegisterFaction(null));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Domain: control management
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void SetNodeController_AutoRegisters_Faction()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            Assert.That(fs.HasFaction("empire"), Is.True);
        }

        [Test]
        public void GetNodeController_ReturnsNull_ForUncontrolledNode()
        {
            var fs = MakeSystem();
            Assert.That(fs.GetNodeController("earth"), Is.Null);
        }

        [Test]
        public void GetNodeController_ReturnsCorrectFaction()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            Assert.That(fs.GetNodeController("earth"), Is.EqualTo("empire"));
        }

        [Test]
        public void TransferControl_UpdatesController()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.TransferControl("earth", "rebels");
            Assert.That(fs.GetNodeController("earth"), Is.EqualTo("rebels"));
        }

        [Test]
        public void TransferControl_FiresCallback()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");

            string capturedNode = null;
            string capturedFaction = null;
            fs.OnControlTransferred = (n, f) => { capturedNode = n; capturedFaction = f; };

            fs.TransferControl("earth", "rebels");

            Assert.That(capturedNode,    Is.EqualTo("earth"));
            Assert.That(capturedFaction, Is.EqualTo("rebels"));
        }

        [Test]
        public void SetNodeController_DoesNot_FireCallback()
        {
            var fs = MakeSystem();
            bool fired = false;
            fs.OnControlTransferred = (_, __) => fired = true;

            fs.SetNodeController("earth", "empire"); // setup call, no callback
            Assert.That(fired, Is.False);
        }

        [Test]
        public void TransferControl_AutoRegisters_TargetFaction()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.TransferControl("earth", "new_faction");
            Assert.That(fs.HasFaction("new_faction"), Is.True);
        }

        [Test]
        public void ClearNodeController_RemovesNode_FromAggregates()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0.2f);
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").ControlledNodeCount, Is.EqualTo(1));

            fs.ClearNodeController("earth");
            fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").ControlledNodeCount, Is.EqualTo(0));
        }

        [Test]
        public void TransferControl_UpdatesAggregatesOnNextTick()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeController("mars",  "rebels");
            fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").ControlledNodeCount, Is.EqualTo(1));
            Assert.That(fs.GetFaction("rebels").ControlledNodeCount, Is.EqualTo(1));

            fs.TransferControl("earth", "rebels");
            fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").ControlledNodeCount, Is.EqualTo(0));
            Assert.That(fs.GetFaction("rebels").ControlledNodeCount, Is.EqualTo(2));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Integration: war-to-faction bridge
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Integration_WarExposure_FeedsExhaustion_ViaSetNodeWarExposure()
        {
            // Demonstrate the coupling pattern: game layer reads WarSystem and feeds FactionSystem
            var dim = new Dimension();
            dim.AddNode("earth");

            var war = new WarSystem(dim, new FieldProfile("war.exposure")
            {
                PropagationRate = 0f, DecayRate = 0f,
                MinLogAmpClamp = -20f, MaxLogAmpClamp = 20f
            });

            var fs = new FactionSystem();
            fs.SetNodeController("earth", "empire");

            war.DeclareWar("earth");

            for (int i = 0; i < 20; i++)
            {
                war.Tick(1f);
                // Game-layer bridge: read war, feed factions
                fs.SetNodeWarExposure("earth", war.GetExposureLogAmp("earth"));
                fs.Tick(1f);
            }

            Assert.That(fs.GetFaction("empire").WarExhaustion, Is.GreaterThan(0f),
                "War exposure from WarSystem should drive faction WarExhaustion");
        }

        [Test]
        public void Integration_MultipleFactions_HaveIndependentDynamics()
        {
            var fs = MakeSystem();
            // empire: high war
            fs.SetNodeController("earth", "empire");
            fs.SetNodeWarExposure("earth", 2f);
            fs.SetNodeStability("earth", 0.9f);

            // rebels: low war, low stability
            fs.SetNodeController("mars", "rebels");
            fs.SetNodeWarExposure("mars", 0f);
            fs.SetNodeStability("mars", 0.2f);

            for (int i = 0; i < 50; i++) fs.Tick(1f);

            var empire = fs.GetFaction("empire");
            var rebels = fs.GetFaction("rebels");

            Assert.That(empire.WarExhaustion, Is.GreaterThan(rebels.WarExhaustion),
                "Empire (at war) should have more exhaustion than rebels (at peace)");
            Assert.That(rebels.PoliticalStability, Is.LessThan(empire.PoliticalStability),
                "Rebels (low stability) should have lower PoliticalStability");
        }

        [Test]
        public void Integration_ControlTransfer_ImmediatelyReflectedInNextTick()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeController("mars",  "empire");
            fs.SetNodeStability("earth", 0.9f);
            fs.SetNodeStability("mars",  0.9f);
            fs.Tick(1f);
            int countBefore = fs.GetFaction("empire").ControlledNodeCount;

            fs.TransferControl("mars", "rebels");
            fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").ControlledNodeCount,
                Is.EqualTo(countBefore - 1));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Scenario: callbacks
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Scenario_CollapseCallback_Fires_WhenCrisisThresholdCrossed()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0f); // drive stability to 0

            string collapsedFaction = null;
            fs.OnFactionCollapse = id => collapsedFaction = id;

            // Run until collapse threshold is crossed
            for (int i = 0; i < 500; i++) fs.Tick(1f);

            Assert.That(collapsedFaction, Is.EqualTo("empire"),
                "OnFactionCollapse should fire when stability crosses below crisis threshold");
        }

        [Test]
        public void Scenario_CollapseCallback_FiresOnce_NotEveryTick()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0f);

            int fireCount = 0;
            fs.OnFactionCollapse = _ => fireCount++;

            for (int i = 0; i < 500; i++) fs.Tick(1f);

            Assert.That(fireCount, Is.EqualTo(1),
                "OnFactionCollapse should fire exactly once per threshold crossing, not every tick");
        }

        [Test]
        public void Scenario_StabilizeCallback_Fires_WhenRecovering()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0f);

            // Collapse first
            for (int i = 0; i < 500; i++) fs.Tick(1f);
            Assert.That(fs.GetFaction("empire").IsCollapsing, Is.True);

            // Now recover
            string stabilizedFaction = null;
            fs.OnFactionStabilize = id => stabilizedFaction = id;
            fs.SetNodeStability("earth", 1f);
            for (int i = 0; i < 500; i++) fs.Tick(1f);

            Assert.That(stabilizedFaction, Is.EqualTo("empire"),
                "OnFactionStabilize should fire when stability crosses above the stable threshold");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Scenario: multi-tick invariants
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Scenario_100Ticks_NoNaN_NoInfinity()
        {
            var fs = new FactionSystem();
            var factions = new[] { "alpha", "beta", "gamma" };
            var nodes    = new[] { "a", "b", "c", "d", "e", "f" };

            fs.SetNodeController("a", "alpha");
            fs.SetNodeController("b", "alpha");
            fs.SetNodeController("c", "beta");
            fs.SetNodeController("d", "beta");
            fs.SetNodeController("e", "gamma");
            fs.SetNodeController("f", "gamma");

            fs.SetNodeStability("a", 0.8f); fs.SetNodeWarExposure("a", 0.2f);
            fs.SetNodeStability("b", 0.5f); fs.SetNodeWarExposure("b", 1.5f);
            fs.SetNodeStability("c", 0.3f); fs.SetNodeWarExposure("c", 0.8f);
            fs.SetNodeStability("d", 0.9f); fs.SetNodeWarExposure("d", 0f);
            fs.SetNodeStability("e", 0.1f); fs.SetNodeWarExposure("e", 3f);
            fs.SetNodeStability("f", 0.6f); fs.SetNodeWarExposure("f", 0.1f);

            for (int tick = 0; tick < 100; tick++)
            {
                if (tick == 30) fs.TransferControl("a", "beta");
                if (tick == 60) fs.TransferControl("c", "alpha");

                fs.Tick(1f);

                foreach (var factionId in factions)
                {
                    var f = fs.GetFaction(factionId);
                    if (f == null) continue;
                    Assert.That(float.IsNaN(f.PoliticalStability),   Is.False, $"NaN stability at tick {tick}");
                    Assert.That(float.IsNaN(f.WarExhaustion),        Is.False, $"NaN exhaustion at tick {tick}");
                    Assert.That(float.IsInfinity(f.PoliticalStability), Is.False, $"Inf stability at tick {tick}");
                    Assert.That(float.IsInfinity(f.WarExhaustion),   Is.False, $"Inf exhaustion at tick {tick}");
                    Assert.That(f.PoliticalStability, Is.InRange(0f, 1f), $"Stability out of range at tick {tick}");
                    Assert.That(f.WarExhaustion,      Is.InRange(0f, 1f), $"Exhaustion out of range at tick {tick}");
                }
            }
        }

        [Test]
        public void Scenario_PoliticalStability_LongRunConvergence()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeController("mars",  "empire");
            fs.SetNodeStability("earth", 0.4f);
            fs.SetNodeStability("mars",  0.6f);
            // Target average = 0.5

            for (int i = 0; i < 2000; i++) fs.Tick(1f);

            Assert.That(fs.GetFaction("empire").PoliticalStability,
                Is.EqualTo(0.5f).Within(0.01f),
                "PoliticalStability should converge to mean node stability (0.5) over many ticks");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Determinism
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameInputs_ProduceSameStability()
        {
            float RunSimulation()
            {
                var fs = new FactionSystem();
                fs.SetNodeController("a", "alpha");
                fs.SetNodeController("b", "beta");
                fs.SetNodeController("c", "alpha");
                fs.SetNodeStability("a", 0.7f);
                fs.SetNodeStability("b", 0.3f);
                fs.SetNodeStability("c", 0.5f);
                fs.SetNodeWarExposure("a", 0.5f);
                for (int i = 0; i < 50; i++) fs.Tick(1f);
                return fs.GetFaction("alpha").PoliticalStability;
            }

            float a = RunSimulation();
            float b = RunSimulation();
            Assert.That(a, Is.EqualTo(b).Within(1e-7f),
                "Identical runs must produce bit-identical results");
        }

        [Test]
        public void Determinism_FactionRegistrationOrder_DoesNotAffectResult()
        {
            // Same factions registered in different order
            float Run(string first, string second)
            {
                var fs = new FactionSystem();
                fs.RegisterFaction(first);
                fs.RegisterFaction(second);
                fs.SetNodeController("earth", "alpha");
                fs.SetNodeController("mars",  "beta");
                fs.SetNodeStability("earth", 0.6f);
                fs.SetNodeStability("mars",  0.4f);
                for (int i = 0; i < 30; i++) fs.Tick(1f);
                return fs.GetFaction("alpha").PoliticalStability
                     + fs.GetFaction("beta").PoliticalStability;
            }

            float ab = Run("alpha", "beta");
            float ba = Run("beta", "alpha");
            Assert.That(ab, Is.EqualTo(ba).Within(1e-5f),
                "Faction registration order must not affect simulation result");
        }

        [Test]
        public void Determinism_NodeAssignmentOrder_DoesNotAffectAggregates()
        {
            float RunOrder(string first, string second)
            {
                var fs = new FactionSystem();
                fs.SetNodeController(first,  "empire");
                fs.SetNodeController(second, "empire");
                fs.SetNodeStability("earth", 0.8f);
                fs.SetNodeStability("mars",  0.4f);
                fs.Tick(1f);
                return fs.GetFaction("empire").AverageStability;
            }

            float em = RunOrder("earth", "mars");
            float me = RunOrder("mars",  "earth");
            Assert.That(em, Is.EqualTo(me).Within(1e-5f),
                "Node assignment order must not affect aggregate computation");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Edge: invalid inputs
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void SetNodeController_NullNodeId_Throws()
        {
            var fs = MakeSystem();
            Assert.Throws<ArgumentException>(() => fs.SetNodeController(null, "empire"));
        }

        [Test]
        public void SetNodeController_NullFactionId_Throws()
        {
            var fs = MakeSystem();
            Assert.Throws<ArgumentException>(() => fs.SetNodeController("earth", null));
        }

        [Test]
        public void TransferControl_EmptyFactionId_Throws()
        {
            var fs = MakeSystem();
            Assert.Throws<ArgumentException>(() => fs.TransferControl("earth", ""));
        }

        [Test]
        public void Tick_ZeroDt_IsNoOp()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0.2f);
            float before = fs.GetFaction("empire").PoliticalStability;
            fs.Tick(0f);
            Assert.That(fs.GetFaction("empire").PoliticalStability, Is.EqualTo(before));
        }

        [Test]
        public void Tick_NegativeDt_IsNoOp()
        {
            var fs = MakeSystem();
            fs.SetNodeController("earth", "empire");
            fs.SetNodeStability("earth", 0.2f);
            float before = fs.GetFaction("empire").PoliticalStability;
            fs.Tick(-5f);
            Assert.That(fs.GetFaction("empire").PoliticalStability, Is.EqualTo(before));
        }
    }
}
