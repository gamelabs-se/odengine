using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;
using Odengine.Faction;
using Odengine.War;

namespace Odengine.Tests.Faction
{
    /// <summary>
    /// Tests for FactionSystem (presence / influence / stability fields).
    ///
    /// Tiers:
    ///   Core        — field read/write, dominance derivation, contested detection
    ///   Domain      — impulse API, observation methods, tick propagation
    ///   Integration — war.exposure → presence coupling, multi-system interaction
    ///   Scenario    — multi-tick long-horizon, dominance flips, power vacuums
    ///   Determinism — sorted iteration, tick-splitting invariance
    ///   Edge        — null / empty inputs, boundary values, ungoverned nodes
    /// </summary>
    [TestFixture]
    public class Faction_FactionSystemTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static FieldProfile MakeProfile(string id,
            float propagation = 0f, float decay = 0f,
            float minClamp = -20f, float maxClamp = 20f) =>
            new FieldProfile(id)
            {
                PropagationRate = propagation,
                DecayRate = decay,
                EdgeResistanceScale = 1f,
                MinLogAmpClamp = minClamp,
                MaxLogAmpClamp = maxClamp
            };

        private static Dimension EmptyDim() => new Dimension();

        private static FactionSystem MakeSystem(Dimension dim = null)
        {
            dim = dim ?? EmptyDim();
            return new FactionSystem(
                dim,
                MakeProfile("faction.presence"),
                MakeProfile("faction.influence"),
                MakeProfile("faction.stability"));
        }

        private static Dimension MakeTwoNodeDim(float resistance = 0.5f)
        {
            var dim = new Dimension();
            dim.AddNode("earth");
            dim.AddNode("mars");
            dim.AddEdge("earth", "mars", resistance);
            dim.AddEdge("mars", "earth", resistance);
            return dim;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: neutral baseline
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Baseline_NewNode_DominantFaction_IsNull()
        {
            var fs = MakeSystem();
            Assert.That(fs.GetDominantFaction("earth"), Is.Null);
        }

        [Test]
        public void Baseline_NewNode_TotalPresence_IsZero()
        {
            var fs = MakeSystem();
            Assert.That(fs.GetTotalPresenceLogAmp("earth"), Is.EqualTo(0f));
        }

        [Test]
        public void Baseline_NewNode_IsContested_IsFalse()
        {
            var fs = MakeSystem();
            Assert.That(fs.IsContested("earth"), Is.False);
        }

        [Test]
        public void Baseline_NewNode_PresenceMultiplier_IsOne()
        {
            var fs = MakeSystem();
            Assert.That(fs.GetPresenceMultiplier("earth", "empire_red"), Is.EqualTo(1f).Within(1e-6f));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: AddPresence / GetDominantFaction
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void AddPresence_SingleFaction_IsDominant()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.5f);
            Assert.That(fs.GetDominantFaction("earth"), Is.EqualTo("empire_red"));
        }

        [Test]
        public void AddPresence_HigherFaction_BecomesNewDominant()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.AddPresence("earth", "pirates", 1.5f);
            Assert.That(fs.GetDominantFaction("earth"), Is.EqualTo("pirates"));
        }

        [Test]
        public void AddPresence_NegativeDelta_ErodesPresence()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 2.0f);
            fs.AddPresence("earth", "empire_red", -2.0f);
            Assert.That(fs.GetDominantFaction("earth"), Is.Null);
        }

        [Test]
        public void AddPresence_NegativeOnly_DoesNotCreateDominance()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", -1.0f);
            Assert.That(fs.GetDominantFaction("earth"), Is.Null,
                "Negative logAmp is below baseline — not dominant");
        }

        [Test]
        public void GetDominantFaction_Deterministic_OnTie()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_zzz", 1.0f);
            fs.AddPresence("earth", "empire_aaa", 1.0f);
            Assert.That(fs.GetDominantFaction("earth"), Is.EqualTo("empire_aaa"),
                "Tie-breaking: Ordinal-first channel wins");
        }

        [Test]
        public void GetDominantFaction_NullNodeId_ReturnsNull()
        {
            var fs = MakeSystem();
            Assert.That(fs.GetDominantFaction(null), Is.Null);
        }

        [Test]
        public void GetDominantFaction_UnknownNode_ReturnsNull()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            Assert.That(fs.GetDominantFaction("mars"), Is.Null);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: TotalPresenceLogAmp
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void TotalPresence_SingleFaction_EqualsItsLogAmp()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.5f);
            Assert.That(fs.GetTotalPresenceLogAmp("earth"), Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void TotalPresence_MultipleFactions_IsSumOfLogAmps()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.AddPresence("earth", "empire_blue", 2.0f);
            Assert.That(fs.GetTotalPresenceLogAmp("earth"), Is.EqualTo(3.0f).Within(1e-5f));
        }

        [Test]
        public void TotalPresence_ZeroAfterFullErosion()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.AddPresence("earth", "empire_red", -1.0f);
            Assert.That(fs.GetTotalPresenceLogAmp("earth"), Is.EqualTo(0f).Within(1e-5f));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core: IsContested
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void IsContested_OneFaction_IsFalse()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 2.0f);
            Assert.That(fs.IsContested("earth"), Is.False);
        }

        [Test]
        public void IsContested_TwoFactionsFarApart_IsFalse()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 2.0f);
            fs.AddPresence("earth", "empire_blue", 0.5f); // gap = 1.5 > default 0.3
            Assert.That(fs.IsContested("earth"), Is.False);
        }

        [Test]
        public void IsContested_TwoFactionsCloselyMatched_IsTrue()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.AddPresence("earth", "empire_blue", 0.8f); // gap = 0.2 < 0.3
            Assert.That(fs.IsContested("earth"), Is.True);
        }

        [Test]
        public void IsContested_OneNegativePresence_IsFalse()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.AddPresence("earth", "empire_blue", -1.0f);
            Assert.That(fs.IsContested("earth"), Is.False,
                "Faction below baseline is not a valid contender");
        }

        [Test]
        public void IsContested_CustomGapThreshold_Respected()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.AddPresence("earth", "empire_blue", 0.5f); // gap = 0.5
            Assert.That(fs.IsContested("earth", gapThreshold: 0.3f), Is.False);
            Assert.That(fs.IsContested("earth", gapThreshold: 0.6f), Is.True);
        }

        [Test]
        public void IsContested_EmptyNode_IsFalse()
        {
            var fs = MakeSystem();
            Assert.That(fs.IsContested("void"), Is.False);
        }

        [Test]
        public void IsContested_NullNode_IsFalse()
        {
            var fs = MakeSystem();
            Assert.That(fs.IsContested(null), Is.False);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Domain: influence and stability fields
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void AddInfluence_IsReflectedInMultiplier()
        {
            var fs = MakeSystem();
            fs.AddInfluence("earth", "trade_guild", 1.0f);
            Assert.That(fs.GetInfluenceMultiplier("earth", "trade_guild"),
                Is.EqualTo(MathF.Exp(1.0f)).Within(1e-5f));
        }

        [Test]
        public void AddStability_IsReflectedInMultiplier()
        {
            var fs = MakeSystem();
            fs.AddStability("earth", "empire_red", 0.5f);
            Assert.That(fs.GetStabilityMultiplier("earth", "empire_red"),
                Is.EqualTo(MathF.Exp(0.5f)).Within(1e-5f));
        }

        [Test]
        public void Fields_AreIndependent_NoCrossContamination()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 2.0f);
            Assert.That(fs.GetInfluenceMultiplier("earth", "empire_red"), Is.EqualTo(1.0f).Within(1e-6f));
            Assert.That(fs.GetStabilityMultiplier("earth", "empire_red"), Is.EqualTo(1.0f).Within(1e-6f));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Domain: Tick
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Tick_ZeroDt_IsNoOp()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            float before = fs.GetPresenceMultiplier("earth", "empire_red");
            fs.Tick(0f);
            Assert.That(fs.GetPresenceMultiplier("earth", "empire_red"), Is.EqualTo(before).Within(1e-6f));
        }

        [Test]
        public void Tick_NegativeDt_IsNoOp()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            float before = fs.GetPresenceMultiplier("earth", "empire_red");
            fs.Tick(-1f);
            Assert.That(fs.GetPresenceMultiplier("earth", "empire_red"), Is.EqualTo(before).Within(1e-6f));
        }

        [Test]
        public void Tick_WithPropagation_SpreadsPresenceAlongEdge()
        {
            var dim = new Dimension();
            dim.AddNode("earth");
            dim.AddNode("mars");
            dim.AddEdge("earth", "mars", 0f);
            dim.AddEdge("mars", "earth", 0f);

            var fs = new FactionSystem(
                dim,
                MakeProfile("faction.presence", propagation: 0.5f),
                MakeProfile("faction.influence"),
                MakeProfile("faction.stability"));

            fs.AddPresence("earth", "empire_red", 2.0f);
            for (int i = 0; i < 10; i++) fs.Tick(1f);

            Assert.That(fs.Presence.GetLogAmp("mars", "empire_red"), Is.GreaterThan(0f),
                "Presence should propagate from earth to mars along a zero-resistance edge");
        }

        [Test]
        public void Tick_WithDecay_PresenceDecaysOverTime()
        {
            var fs = new FactionSystem(
                EmptyDim(),
                MakeProfile("faction.presence", propagation: 0f, decay: 0.1f),
                MakeProfile("faction.influence"),
                MakeProfile("faction.stability"));

            fs.AddPresence("earth", "empire_red", 2.0f);
            float before = fs.Presence.GetLogAmp("earth", "empire_red");
            for (int i = 0; i < 10; i++) fs.Tick(1f);
            Assert.That(fs.Presence.GetLogAmp("earth", "empire_red"), Is.LessThan(before));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Domain: OnDominanceChanged callback
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void OnDominanceChanged_Fires_WhenFactionFirstGainsDominance()
        {
            var fs = MakeSystem();
            (string node, string prev, string next) ev = default;
            int count = 0;
            fs.OnDominanceChanged = (n, p, nx) => { ev = (n, p, nx); count++; };

            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.Tick(1f);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(ev.node, Is.EqualTo("earth"));
            Assert.That(ev.prev, Is.Null);
            Assert.That(ev.next, Is.EqualTo("empire_red"));
        }

        [Test]
        public void OnDominanceChanged_Fires_WhenDominanceFlips()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 2.0f);
            fs.Tick(1f);

            var events = new List<(string, string, string)>();
            fs.OnDominanceChanged = (n, p, nx) => events.Add((n, p, nx));
            fs.AddPresence("earth", "pirates", 3.0f);
            fs.Tick(1f);

            Assert.That(events.Count, Is.EqualTo(1));
            var (node, prev, next) = events[0];
            Assert.That(node, Is.EqualTo("earth"));
            Assert.That(prev, Is.EqualTo("empire_red"));
            Assert.That(next, Is.EqualTo("pirates"));
        }

        [Test]
        public void OnDominanceChanged_Fires_WhenNodeBecomesUngoverned()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.Tick(1f);

            string capturedNext = "sentinel";
            fs.OnDominanceChanged = (_, _, nx) => capturedNext = nx;
            fs.AddPresence("earth", "empire_red", -1.0f);
            fs.Tick(1f);

            Assert.That(capturedNext, Is.Null);
        }

        [Test]
        public void OnDominanceChanged_DoesNotFire_WhenDominanceUnchanged()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 2.0f);
            fs.Tick(1f);

            int count = 0;
            fs.OnDominanceChanged = (_, _, _) => count++;
            fs.AddPresence("earth", "empire_red", 0.5f);
            fs.Tick(1f);

            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void OnDominanceChanged_Null_DoesNotThrow()
        {
            var fs = MakeSystem();
            fs.OnDominanceChanged = null;
            fs.AddPresence("earth", "empire_red", 1.0f);
            Assert.DoesNotThrow(() => fs.Tick(1f));
        }

        [Test]
        public void OnDominanceChanged_MultipleNodes_EachFiresIndependently()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 1.0f);
            fs.AddPresence("mars", "empire_blue", 1.0f);

            var events = new List<string>();
            fs.OnDominanceChanged = (n, _, _) => events.Add(n);
            fs.Tick(1f);

            Assert.That(events, Does.Contain("earth"));
            Assert.That(events, Does.Contain("mars"));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Integration: war → faction coupling pattern
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Integration_WarErodesPresence_ViaCouplingPattern()
        {
            var warDim = new Dimension();
            warDim.AddNode("frontier");
            var war = new WarSystem(warDim, new FieldProfile("war.exposure")
            {
                PropagationRate = 0f,
                DecayRate = 0f,
                MinLogAmpClamp = -20f,
                MaxLogAmpClamp = 20f
            });

            var fs = MakeSystem();
            fs.AddPresence("frontier", "empire_red", 3.0f);

            war.DeclareWar("frontier");
            const float drain = 0.02f;

            for (int i = 0; i < 30; i++)
            {
                war.Tick(1f);
                fs.AddPresence("frontier", "empire_red", -drain * war.GetExposureLogAmp("frontier"));
                fs.Tick(1f);
            }

            Assert.That(fs.Presence.GetLogAmp("frontier", "empire_red"), Is.LessThan(3.0f),
                "Sustained war exposure should erode faction presence");
        }

        [Test]
        public void Integration_WarErodesAllFactions_NotJustOne()
        {
            var fs = MakeSystem();
            fs.AddPresence("frontier", "empire_red", 2.0f);
            fs.AddPresence("frontier", "empire_blue", 2.0f);

            float warImpulse = 0.5f;
            fs.AddPresence("frontier", "empire_red", -warImpulse);
            fs.AddPresence("frontier", "empire_blue", -warImpulse);
            fs.Tick(1f);

            Assert.That(fs.Presence.GetLogAmp("frontier", "empire_red"), Is.LessThan(2.0f));
            Assert.That(fs.Presence.GetLogAmp("frontier", "empire_blue"), Is.LessThan(2.0f));
        }

        [Test]
        public void Integration_Conquest_FlipsDominance()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 2.0f);
            Assert.That(fs.GetDominantFaction("earth"), Is.EqualTo("empire_red"));

            for (int i = 0; i < 20; i++)
            {
                fs.AddPresence("earth", "empire_red", -0.15f);
                fs.AddPresence("earth", "empire_blue", 0.2f);
                fs.Tick(1f);
            }

            Assert.That(fs.GetDominantFaction("earth"), Is.EqualTo("empire_blue"),
                "Blue presence overtakes red through sustained campaign");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Scenario
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Scenario_500Ticks_NoNaN_NoInfinity()
        {
            var fs = MakeSystem();
            fs.AddPresence("earth", "empire_red", 2.0f);
            fs.AddPresence("mars", "empire_blue", 1.8f);
            fs.AddPresence("frontier", "empire_red", 0.8f);
            fs.AddPresence("frontier", "empire_blue", 0.9f);

            var nodes = new[] { "earth", "mars", "frontier" };
            var factions = new[] { "empire_red", "empire_blue" };

            for (int tick = 0; tick < 500; tick++)
            {
                if (tick % 50 == 0)
                {
                    fs.AddPresence("frontier", "empire_red", -0.1f);
                    fs.AddPresence("frontier", "empire_blue", 0.1f);
                }
                fs.Tick(1f);

                foreach (var node in nodes)
                {
                    foreach (var faction in factions)
                    {
                        float p = fs.Presence.GetLogAmp(node, faction);
                        Assert.That(float.IsNaN(p), Is.False, $"NaN at tick {tick} {node}/{faction}");
                        Assert.That(float.IsInfinity(p), Is.False, $"Inf at tick {tick} {node}/{faction}");
                    }
                }
            }
        }

        [Test]
        public void Scenario_PowerVacuum_WhenPresenceDecaysToZero()
        {
            var fs = new FactionSystem(
                EmptyDim(),
                MakeProfile("faction.presence", decay: 0.5f),
                MakeProfile("faction.influence"),
                MakeProfile("faction.stability"));

            fs.AddPresence("outpost", "empire_red", 0.5f);
            for (int i = 0; i < 100; i++) fs.Tick(1f);

            Assert.That(fs.GetDominantFaction("outpost"), Is.Null,
                "Presence decays fully → ungoverned node");
            Assert.That(fs.GetTotalPresenceLogAmp("outpost"), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Scenario_ContestedNode_DominanceCanOscillate()
        {
            var fs = MakeSystem();
            var flips = new List<(string prev, string next)>();
            fs.OnDominanceChanged = (_, p, nx) => flips.Add((p, nx));

            fs.AddPresence("border", "empire_red", 1.0f);
            fs.AddPresence("border", "empire_blue", 0.9f);
            fs.Tick(1f);

            for (int i = 0; i < 100; i++)
            {
                if (i % 10 < 5) fs.AddPresence("border", "empire_blue", 0.05f);
                else fs.AddPresence("border", "empire_red", 0.05f);
                fs.Tick(1f);
            }

            Assert.That(flips.Count, Is.GreaterThan(1),
                "Alternating reinforcement should cause dominance to flip multiple times");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Determinism
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameInputs_ProduceSamePresenceLogAmp()
        {
            float Run()
            {
                var fs = MakeSystem();
                fs.AddPresence("earth", "empire_red", 1.5f);
                fs.AddPresence("earth", "empire_blue", 0.8f);
                for (int i = 0; i < 50; i++)
                {
                    fs.AddPresence("earth", "empire_red", 0.01f);
                    fs.Tick(1f);
                }
                return fs.Presence.GetLogAmp("earth", "empire_red");
            }

            Assert.That(Run(), Is.EqualTo(Run()).Within(1e-7f));
        }

        [Test]
        public void Determinism_InsertionOrder_DoesNotAffectDominance()
        {
            string RunOrder(string first, string second)
            {
                var fs = MakeSystem();
                fs.AddPresence("node", first, 1.0f);
                fs.AddPresence("node", second, 1.0f); // equal logAmps — tests Ordinal tie-break
                return fs.GetDominantFaction("node");
            }

            Assert.That(RunOrder("empire_alpha", "empire_beta"), Is.EqualTo("empire_alpha"));
            Assert.That(RunOrder("empire_beta", "empire_alpha"), Is.EqualTo("empire_alpha"),
                "Dominance must not depend on insertion order");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Edge cases
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Construction_NullDimension_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FactionSystem(null, MakeProfile("p"), MakeProfile("i"), MakeProfile("s")));
        }

        [Test]
        public void Construction_NullPresenceProfile_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FactionSystem(EmptyDim(), null, MakeProfile("i"), MakeProfile("s")));
        }

        [Test]
        public void Construction_NullInfluenceProfile_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FactionSystem(EmptyDim(), MakeProfile("p"), null, MakeProfile("s")));
        }

        [Test]
        public void Construction_NullStabilityProfile_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FactionSystem(EmptyDim(), MakeProfile("p"), MakeProfile("i"), null));
        }

        [Test]
        public void AddPresence_EmptyFactionId_DoesNotThrow()
        {
            var fs = MakeSystem();
            Assert.DoesNotThrow(() => fs.AddPresence("earth", "", 1.0f));
        }

        [Test]
        public void AddPresence_EmptyNodeId_DoesNotThrow()
        {
            var fs = MakeSystem();
            Assert.DoesNotThrow(() => fs.AddPresence("", "empire_red", 1.0f));
        }

        [Test]
        public void GetPresenceMultiplier_NeutralNode_IsOne()
        {
            var fs = MakeSystem();
            Assert.That(fs.GetPresenceMultiplier("any_node", "any_faction"), Is.EqualTo(1.0f).Within(1e-6f));
        }

        [Test]
        public void FieldsRegisteredInDimension_AreAccessibleViaName()
        {
            var dim = EmptyDim();
            _ = MakeSystem(dim);
            Assert.That(dim.GetField("faction.presence"), Is.Not.Null);
            Assert.That(dim.GetField("faction.influence"), Is.Not.Null);
            Assert.That(dim.GetField("faction.stability"), Is.Not.Null);
        }
    }
}
