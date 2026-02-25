using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;
using Odengine.Intel;
using Odengine.Tests.Shared;

namespace Odengine.Tests.Modules.Intel
{
    [TestFixture]
    public class Intel_IntelSystemTests
    {
        // ── Helpers ────────────────────────────────────────────────────────

        private static FieldProfile MakeProfile(
            string id = "intel.coverage",
            float decay = 0.05f,
            float propagation = 0f,
            float edgeResistance = 1f) =>
            new FieldProfile(id)
            {
                LogEpsilon = 0.0001f,
                DecayRate = decay,
                PropagationRate = propagation,
                EdgeResistanceScale = edgeResistance,
                MinLogAmpClamp = -10f,
                MaxLogAmpClamp = 10f,
            };

        private static Dimension MakeDimension(params string[] nodeIds)
        {
            var dim = new Dimension();
            foreach (var id in nodeIds) dim.AddNode(id);
            return dim;
        }

        private static IntelSystem MakeIntel(
            Dimension dim,
            float decay = 0.05f,
            float propagation = 0f,
            float threshold = 0.0001f,
            float edgeResistance = 1f)
        {
            var profile = MakeProfile(decay: decay, propagation: propagation, edgeResistance: edgeResistance);
            var config = new IntelConfig { ActiveCoverageThreshold = threshold };
            return new IntelSystem(dim, profile, config);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Construction
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Constructor_RegistersCoverageFieldInDimension()
        {
            var dim = MakeDimension("a");
            var intel = MakeIntel(dim);
            // No throw means the field was registered
            Assert.DoesNotThrow(() => dim.GetField("intel.coverage"));
        }

        [Test]
        public void Constructor_NullDimension_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new IntelSystem(null, MakeProfile()));
        }

        [Test]
        public void Constructor_NullProfile_Throws()
        {
            var dim = MakeDimension("a");
            Assert.Throws<ArgumentNullException>(() =>
                new IntelSystem(dim, null));
        }

        [Test]
        public void Constructor_DefaultConfig_ThresholdApplied()
        {
            var dim = MakeDimension("a");
            var intel = new IntelSystem(dim, MakeProfile());
            // Below default ActiveCoverageThreshold = 0.0001f
            intel.DeploySensor("a", "red", 0.00005f);
            Assert.That(intel.IsTracked("a", "red"), Is.False);
        }

        // ═══════════════════════════════════════════════════════════════════
        // DeploySensor
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void DeploySensor_SetsLogAmpAtNode()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 2f);

            Assert.That(intel.Coverage.GetLogAmp("hub", "red"), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void DeploySensor_Accumulates_MultipleCallsSameFaction()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 1f);
            intel.DeploySensor("hub", "red", 0.5f);

            Assert.That(intel.Coverage.GetLogAmp("hub", "red"), Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void DeploySensor_MultipleFactions_IndependentChannels()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 2f);
            intel.DeploySensor("hub", "blue", 1.5f);

            Assert.That(intel.Coverage.GetLogAmp("hub", "red"), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(intel.Coverage.GetLogAmp("hub", "blue"), Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void DeploySensor_MultipleNodes_IndependentEntries()
        {
            var dim = MakeDimension("north", "south");
            var intel = MakeIntel(dim);
            intel.DeploySensor("north", "red", 3f);
            intel.DeploySensor("south", "red", 1f);

            Assert.That(intel.Coverage.GetLogAmp("north", "red"), Is.EqualTo(3f).Within(1e-5f));
            Assert.That(intel.Coverage.GetLogAmp("south", "red"), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void DeploySensor_EmptyNodeId_Throws()
        {
            var dim = MakeDimension("a");
            var intel = MakeIntel(dim);
            Assert.Throws<ArgumentException>(() => intel.DeploySensor("", "red", 1f));
        }

        [Test]
        public void DeploySensor_EmptyFactionId_Throws()
        {
            var dim = MakeDimension("a");
            var intel = MakeIntel(dim);
            Assert.Throws<ArgumentException>(() => intel.DeploySensor("a", "", 1f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetCoverage
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetCoverage_NoSensors_ReturnsZero()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            Assert.That(intel.GetCoverage("hub", "red"), Is.EqualTo(0f).Within(1e-7f));
        }

        [Test]
        public void GetCoverage_AfterDeploySensor_MatchesLogAmp()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 1.8f);
            Assert.That(intel.GetCoverage("hub", "red"), Is.EqualTo(1.8f).Within(1e-5f));
        }

        [Test]
        public void GetCoverage_EmptyNodeId_Throws()
        {
            var dim = MakeDimension("a");
            var intel = MakeIntel(dim);
            Assert.Throws<ArgumentException>(() => intel.GetCoverage("", "red"));
        }

        [Test]
        public void GetCoverage_EmptyFactionId_Throws()
        {
            var dim = MakeDimension("a");
            var intel = MakeIntel(dim);
            Assert.Throws<ArgumentException>(() => intel.GetCoverage("a", ""));
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetCoverageMultiplier
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetCoverageMultiplier_NoCoverage_ReturnsOne()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            Assert.That(intel.GetCoverageMultiplier("hub", "red"), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void GetCoverageMultiplier_MatchesExpLogAmp()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 1f); // exp(1) ≈ 2.718
            Assert.That(intel.GetCoverageMultiplier("hub", "red"),
                Is.EqualTo(MathF.Exp(1f)).Within(1e-4f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // IsTracked
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void IsTracked_BelowThreshold_ReturnsFalse()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, threshold: 0.5f);
            intel.DeploySensor("hub", "red", 0.3f); // below threshold
            Assert.That(intel.IsTracked("hub", "red"), Is.False);
        }

        [Test]
        public void IsTracked_AtOrAboveThreshold_ReturnsTrue()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, threshold: 0.5f);
            intel.DeploySensor("hub", "red", 0.5f); // exactly at threshold
            Assert.That(intel.IsTracked("hub", "red"), Is.True);
        }

        [Test]
        public void IsTracked_NoSensors_ReturnsFalse()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            Assert.That(intel.IsTracked("hub", "red"), Is.False);
        }

        [Test]
        public void IsTracked_OneFactionTracked_OtherNot()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, threshold: 0.5f);
            intel.DeploySensor("hub", "red", 2f);
            // blue has no sensors

            Assert.That(intel.IsTracked("hub", "red"), Is.True);
            Assert.That(intel.IsTracked("hub", "blue"), Is.False);
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetDominantObserver
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetDominantObserver_NoCoverage_ReturnsNull()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            Assert.That(intel.GetDominantObserver("hub"), Is.Null);
        }

        [Test]
        public void GetDominantObserver_SingleFaction_ReturnsThatFaction()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 2f);
            Assert.That(intel.GetDominantObserver("hub"), Is.EqualTo("red"));
        }

        [Test]
        public void GetDominantObserver_ReturnsHighestLogAmpFaction()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 1f);
            intel.DeploySensor("hub", "blue", 3f);
            Assert.That(intel.GetDominantObserver("hub"), Is.EqualTo("blue"));
        }

        [Test]
        public void GetDominantObserver_DifferentNodes_Independent()
        {
            var dim = MakeDimension("north", "south");
            var intel = MakeIntel(dim);
            intel.DeploySensor("north", "red", 3f);
            intel.DeploySensor("south", "blue", 2f);

            Assert.That(intel.GetDominantObserver("north"), Is.EqualTo("red"));
            Assert.That(intel.GetDominantObserver("south"), Is.EqualTo("blue"));
        }

        [Test]
        public void GetDominantObserver_EmptyNodeId_Throws()
        {
            var dim = MakeDimension("a");
            var intel = MakeIntel(dim);
            Assert.Throws<ArgumentException>(() => intel.GetDominantObserver(""));
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetTrackingFactions
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetTrackingFactions_NoCoverage_ReturnsEmpty()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            Assert.That(intel.GetTrackingFactions("hub").Count, Is.EqualTo(0));
        }

        [Test]
        public void GetTrackingFactions_OnlyAboveThreshold()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, threshold: 1f);
            intel.DeploySensor("hub", "red", 2f);   // above
            intel.DeploySensor("hub", "blue", 0.5f); // below threshold

            var factions = intel.GetTrackingFactions("hub");
            Assert.That(factions, Does.Contain("red"));
            Assert.That(factions, Does.Not.Contain("blue"));
        }

        [Test]
        public void GetTrackingFactions_MultipleFactions_AllAboveThreshold()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, threshold: 0.5f);
            intel.DeploySensor("hub", "red", 2f);
            intel.DeploySensor("hub", "blue", 1f);
            intel.DeploySensor("hub", "green", 0.8f);

            var factions = intel.GetTrackingFactions("hub");
            Assert.That(factions.Count, Is.EqualTo(3));
        }

        [Test]
        public void GetTrackingFactions_IsSortedOrdinal()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, threshold: 0.0001f);
            intel.DeploySensor("hub", "zulu", 1f);
            intel.DeploySensor("hub", "alpha", 1f);
            intel.DeploySensor("hub", "mike", 1f);

            var factions = intel.GetTrackingFactions("hub");
            Assert.That(factions[0], Is.EqualTo("alpha"));
            Assert.That(factions[1], Is.EqualTo("mike"));
            Assert.That(factions[2], Is.EqualTo("zulu"));
        }

        [Test]
        public void GetTrackingFactions_EmptyNodeId_Throws()
        {
            var dim = MakeDimension("a");
            var intel = MakeIntel(dim);
            Assert.Throws<ArgumentException>(() => intel.GetTrackingFactions(""));
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetCoveredNodeIds
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetCoveredNodeIds_NoCoverage_ReturnsEmpty()
        {
            var dim = MakeDimension("hub", "port");
            var intel = MakeIntel(dim);
            Assert.That(intel.GetCoveredNodeIds().Count, Is.EqualTo(0));
        }

        [Test]
        public void GetCoveredNodeIds_OnlyActiveNodes()
        {
            var dim = MakeDimension("hub", "port", "frontier");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 1f);
            intel.DeploySensor("frontier", "blue", 1f);
            // "port" has no coverage

            var nodes = intel.GetCoveredNodeIds();
            Assert.That(nodes, Does.Contain("hub"));
            Assert.That(nodes, Does.Contain("frontier"));
            Assert.That(nodes, Does.Not.Contain("port"));
        }

        [Test]
        public void GetCoveredNodeIds_IsSortedOrdinal()
        {
            var dim = MakeDimension("z-world", "a-world", "m-world");
            var intel = MakeIntel(dim);
            intel.DeploySensor("z-world", "red", 1f);
            intel.DeploySensor("a-world", "red", 1f);
            intel.DeploySensor("m-world", "red", 1f);

            var nodes = intel.GetCoveredNodeIds();
            Assert.That(nodes[0], Is.EqualTo("a-world"));
            Assert.That(nodes[1], Is.EqualTo("m-world"));
            Assert.That(nodes[2], Is.EqualTo("z-world"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetTotalCoverage
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetTotalCoverage_NoCoverage_ReturnsZero()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            Assert.That(intel.GetTotalCoverage("hub"), Is.EqualTo(0f).Within(1e-7f));
        }

        [Test]
        public void GetTotalCoverage_SumsAllFactions()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim);
            intel.DeploySensor("hub", "red", 2f);
            intel.DeploySensor("hub", "blue", 1.5f);
            // total = 2.0 + 1.5 = 3.5
            Assert.That(intel.GetTotalCoverage("hub"), Is.EqualTo(3.5f).Within(1e-4f));
        }

        [Test]
        public void GetTotalCoverage_EmptyNodeId_Throws()
        {
            var dim = MakeDimension("a");
            var intel = MakeIntel(dim);
            Assert.Throws<ArgumentException>(() => intel.GetTotalCoverage(""));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Tick — decay
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Tick_ZeroDt_NoCoverageChange()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, decay: 0.5f);
            intel.DeploySensor("hub", "red", 3f);

            intel.Tick(0f);

            Assert.That(intel.Coverage.GetLogAmp("hub", "red"), Is.EqualTo(3f).Within(1e-5f));
        }

        [Test]
        public void Tick_NoPropagation_DecaysOverTime()
        {
            // With propagation=0, decay=0.1, coverage should decrease
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, decay: 0.1f, propagation: 0f);
            intel.DeploySensor("hub", "red", 2f);

            intel.Tick(1f);

            float after = intel.GetCoverage("hub", "red");
            Assert.That(after, Is.LessThan(2f),
                "Coverage should decay without reinforcement.");
            Assert.That(after, Is.GreaterThan(0f),
                "Coverage should not drop to zero immediately.");
        }

        [Test]
        public void Tick_DecayProportionalToDt()
        {
            // Halving dt should produce proportionally less decay
            static float RunTick(float dt)
            {
                var dim = MakeDimension("hub");
                var intel = MakeIntel(dim, decay: 0.2f, propagation: 0f);
                intel.DeploySensor("hub", "red", 4f);
                intel.Tick(dt);
                return intel.GetCoverage("hub", "red");
            }

            float halfDt = RunTick(0.5f);
            float fullDt = RunTick(1.0f);

            // With more time, coverage should be lower
            Assert.That(fullDt, Is.LessThan(halfDt));
        }

        [Test]
        public void Tick_MultipleFactions_IndependentDecay()
        {
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, decay: 0.1f, propagation: 0f);
            intel.DeploySensor("hub", "red", 4f);
            intel.DeploySensor("hub", "blue", 2f);

            intel.Tick(1f);

            float redAfter = intel.GetCoverage("hub", "red");
            float blueAfter = intel.GetCoverage("hub", "blue");

            Assert.That(redAfter, Is.LessThan(4f));
            Assert.That(blueAfter, Is.LessThan(2f));

            // Relative ordering must be preserved (red started higher)
            Assert.That(redAfter, Is.GreaterThan(blueAfter));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Tick — propagation
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Tick_WithPropagation_CoverageSpreadToAdjacentNode()
        {
            var dim = MakeDimension("alpha", "beta");
            dim.AddEdge("alpha", "beta", resistance: 0f);

            var intel = MakeIntel(dim, decay: 0f, propagation: 0.3f, edgeResistance: 1f);
            intel.DeploySensor("alpha", "red", 3f);

            intel.Tick(1f);

            float betaCoverage = intel.GetCoverage("beta", "red");
            Assert.That(betaCoverage, Is.GreaterThan(0f),
                "Coverage should propagate from alpha to beta after one tick.");
        }

        [Test]
        public void Tick_NoPropagation_NoSpread()
        {
            var dim = MakeDimension("alpha", "beta");
            dim.AddEdge("alpha", "beta", resistance: 0f);

            var intel = MakeIntel(dim, decay: 0f, propagation: 0f);
            intel.DeploySensor("alpha", "red", 3f);

            intel.Tick(1f);

            Assert.That(intel.GetCoverage("beta", "red"), Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void Tick_HighResistance_LessPropagation()
        {
            // Compare how much coverage reaches "beta" through a high-resistance edge
            // vs. a low-resistance edge
            float Coverage(float resistance)
            {
                var dim = MakeDimension("alpha", "beta");
                dim.AddEdge("alpha", "beta", resistance: resistance);
                var intel = MakeIntel(dim, decay: 0f, propagation: 0.4f, edgeResistance: 1f);
                intel.DeploySensor("alpha", "red", 3f);
                intel.Tick(1f);
                return intel.GetCoverage("beta", "red");
            }

            float lowRes = Coverage(0f);
            float highRes = Coverage(2f);

            Assert.That(lowRes, Is.GreaterThan(highRes),
                "High-resistance edge should impede coverage propagation.");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Determinism
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Tick_SameInputs_SameStateHash()
        {
            string RunAndHash()
            {
                var dim = MakeDimension("hub", "port", "frontier");
                dim.AddEdge("hub", "port", resistance: 0.5f);
                dim.AddEdge("port", "frontier", resistance: 1f);
                var intel = MakeIntel(dim, decay: 0.08f, propagation: 0.2f);

                intel.DeploySensor("hub", "red", 3f);
                intel.DeploySensor("port", "blue", 2f);
                intel.DeploySensor("frontier", "red", 1f);

                for (int i = 0; i < 20; i++)
                    intel.Tick(0.1f);

                return StateHash.Compute(dim);
            }

            Assert.That(RunAndHash(), Is.EqualTo(RunAndHash()));
        }

        [Test]
        public void MultiTick_NoNanOrInfinity()
        {
            var dim = MakeDimension("hub", "port", "frontier");
            dim.AddEdge("hub", "port", resistance: 0.5f);
            dim.AddEdge("port", "frontier", resistance: 0.8f);

            var intel = MakeIntel(dim, decay: 0.1f, propagation: 0.3f);
            intel.DeploySensor("hub", "red", 4f);
            intel.DeploySensor("port", "blue", 2f);

            for (int tick = 0; tick < 100; tick++)
            {
                intel.Tick(0.1f);

                foreach (var nodeId in intel.GetCoveredNodeIds())
                {
                    foreach (var channel in intel.Coverage.GetActiveChannelIdsSortedForNode(nodeId))
                    {
                        float v = intel.Coverage.GetLogAmp(nodeId, channel);
                        Assert.That(float.IsNaN(v), Is.False, $"NaN at ({nodeId}, {channel}) tick {tick}");
                        Assert.That(float.IsInfinity(v), Is.False, $"Inf at ({nodeId}, {channel}) tick {tick}");
                    }
                }
            }
        }

        [Test]
        public void MultiTick_WithReinforcementEvery10Ticks_CoverageStable()
        {
            // If coverage is reinforced often enough, it should not vanish
            var dim = MakeDimension("hub");
            var intel = MakeIntel(dim, decay: 0.2f, propagation: 0f);

            for (int tick = 0; tick < 50; tick++)
            {
                if (tick % 10 == 0)
                    intel.DeploySensor("hub", "red", 1f);
                intel.Tick(0.1f);
            }

            Assert.That(intel.GetCoverage("hub", "red"), Is.GreaterThan(0f),
                "Regularly reinforced coverage should remain active.");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Integration — CouplingRule feeding into IntelSystem
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Integration_FactionPresenceBoostsIntelCoverage()
        {
            // Faction presence at a node (faction.presence) should drive intel.coverage
            // via a Linear CouplingRule — simulate it directly here.
            var dim = MakeDimension("hub");

            // Build a faction-presence field acting as the source
            var factionProfile = new FieldProfile("faction.presence")
            {
                LogEpsilon = 0.0001f,
                DecayRate = 0f,
                PropagationRate = 0f,
                EdgeResistanceScale = 1f,
                MinLogAmpClamp = -10f,
                MaxLogAmpClamp = 10f,
            };
            var factionField = dim.GetOrCreateField("faction.presence", factionProfile);
            factionField.AddLogAmp("hub", "red", 2f);

            var intel = MakeIntel(dim, threshold: 0.0001f);

            // Manually simulate coupling: faction.presence → intel.coverage
            // Linear(0.05f × dt) applied each tick
            const float couplingScale = 0.05f;
            const float dt = 1f;

            float presenceLogAmp = factionField.GetLogAmp("hub", "red");
            float impulse = presenceLogAmp * couplingScale * dt;
            intel.DeploySensor("hub", "red", impulse);

            Assert.That(intel.IsTracked("hub", "red"), Is.True,
                "Faction presence coupling should bring intel coverage above threshold.");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Stress / Fuzz
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Fuzz_RandomGraph_NoNanOrInfinity()
        {
            var rng = new DeterministicRng(seed: 0xA1B2C3);

            const int nodeCount = 10;
            const int factionCount = 3;
            const int tickCount = 50;

            string[] nodeIds = new string[nodeCount];
            string[] factionIds = new string[factionCount];
            for (int i = 0; i < nodeCount; i++) nodeIds[i] = $"n{i}";
            for (int i = 0; i < factionCount; i++) factionIds[i] = $"f{i}";

            var dim = MakeDimension(nodeIds);
            for (int i = 0; i < nodeCount - 1; i++)
                dim.AddEdge(nodeIds[i], nodeIds[i + 1], resistance: rng.NextFloat(0f, 2f));

            var intel = MakeIntel(dim, decay: 0.1f, propagation: 0.2f);

            // Seed sensors
            for (int i = 0; i < 5; i++)
            {
                string nodeId = nodeIds[rng.NextInt(0, nodeCount)];
                string factionId = factionIds[rng.NextInt(0, factionCount)];
                intel.DeploySensor(nodeId, factionId, rng.NextFloat(0f, 3f));
            }

            for (int tick = 0; tick < tickCount; tick++)
            {
                intel.Tick(0.1f);

                foreach (var nodeId in intel.GetCoveredNodeIds())
                {
                    foreach (var channel in intel.Coverage.GetActiveChannelIdsSortedForNode(nodeId))
                    {
                        float v = intel.Coverage.GetLogAmp(nodeId, channel);
                        Assert.That(float.IsNaN(v), Is.False, $"NaN at tick {tick}");
                        Assert.That(float.IsInfinity(v), Is.False, $"Inf at tick {tick}");
                    }
                }
            }
        }
    }
}
