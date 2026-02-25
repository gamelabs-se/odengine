using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Combat;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Tests.Modules.Combat
{
    [TestFixture]
    public class Combat_CombatSystemTests
    {
        // ── Helpers ────────────────────────────────────────────────────────

        private static FieldProfile MakeProfile(string id, float decay = 0.1f, float propagation = 0f) =>
            new FieldProfile(id)
            {
                LogEpsilon = 0.0001f,
                DecayRate = decay,
                PropagationRate = propagation,
                EdgeResistanceScale = 1f,
                MinLogAmpClamp = -10f,
                MaxLogAmpClamp = 10f,
            };

        private static Dimension MakeDimension(params string[] nodeIds)
        {
            var dim = new Dimension();
            foreach (var id in nodeIds) dim.AddNode(id);
            return dim;
        }

        private static CombatSystem MakeCombat(Dimension dim, float attrition = 0.3f, float decay = 0f)
        {
            var profile = MakeProfile("combat.intensity", decay: decay);
            var config = new CombatConfig { AttritionRate = attrition, ActiveThreshold = 0.0001f };
            return new CombatSystem(dim, profile, config);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Construction
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Constructor_RegistersIntensityFieldInDimension()
        {
            var dim = MakeDimension("a");
            var _ = MakeCombat(dim);
            Assert.DoesNotThrow(() => dim.GetField("combat.intensity"));
        }

        [Test]
        public void Constructor_NullDimension_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new CombatSystem(null, MakeProfile("x")));
        }

        [Test]
        public void Constructor_NullProfile_Throws()
        {
            var dim = MakeDimension("a");
            Assert.Throws<System.ArgumentNullException>(() =>
                new CombatSystem(dim, null));
        }

        // ═══════════════════════════════════════════════════════════════════
        // CommitForce
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void CommitForce_SetsLogAmpAtNode()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 2f);

            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void CommitForce_Accumulates_MultipleCalls()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 1f);
            combat.CommitForce("north", "red", 0.5f);

            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void CommitForce_MultipleFactions_IndependentChannels()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 2f);
            combat.CommitForce("north", "blue", 1.5f);

            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(combat.Intensity.GetLogAmp("north", "blue"), Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void CommitForce_EmptyNodeId_Throws()
        {
            var dim = MakeDimension("a");
            var combat = MakeCombat(dim);
            Assert.Throws<System.ArgumentException>(() => combat.CommitForce("", "red", 1f));
        }

        [Test]
        public void CommitForce_EmptyFactionId_Throws()
        {
            var dim = MakeDimension("a");
            var combat = MakeCombat(dim);
            Assert.Throws<System.ArgumentException>(() => combat.CommitForce("a", "", 1f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetDominantFaction
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetDominantFaction_NoActivity_ReturnsNull()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            Assert.That(combat.GetDominantFaction("north"), Is.Null);
        }

        [Test]
        public void GetDominantFaction_SingleFaction_ReturnsThatFaction()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 2f);
            Assert.That(combat.GetDominantFaction("north"), Is.EqualTo("red"));
        }

        [Test]
        public void GetDominantFaction_ReturnsHighestLogAmp()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 1f);
            combat.CommitForce("north", "blue", 3f);
            Assert.That(combat.GetDominantFaction("north"), Is.EqualTo("blue"));
        }

        [Test]
        public void GetDominantFaction_DifferentNodesIndependent()
        {
            var dim = MakeDimension("north", "south");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 3f);
            combat.CommitForce("south", "blue", 2f);

            Assert.That(combat.GetDominantFaction("north"), Is.EqualTo("red"));
            Assert.That(combat.GetDominantFaction("south"), Is.EqualTo("blue"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetIntensity
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetIntensity_NoActivity_ReturnsZero()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            Assert.That(combat.GetIntensity("north"), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void GetIntensity_SingleFaction_ReturnsItsLogAmp()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 2f);
            Assert.That(combat.GetIntensity("north"), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void GetIntensity_SumsAllActiveFactions()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 2f);
            combat.CommitForce("north", "blue", 1.5f);
            Assert.That(combat.GetIntensity("north"), Is.EqualTo(3.5f).Within(1e-5f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // GetActiveNodeIds
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void GetActiveNodeIds_NoActivity_ReturnsEmpty()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim);
            Assert.That(combat.GetActiveNodeIds().Count, Is.EqualTo(0));
        }

        [Test]
        public void GetActiveNodeIds_ReturnsOnlyActiveNodes()
        {
            var dim = MakeDimension("north", "south", "east");
            var combat = MakeCombat(dim);
            combat.CommitForce("north", "red", 1f);
            combat.CommitForce("east", "blue", 1f);
            // "south" has no activity

            var active = combat.GetActiveNodeIds();
            Assert.That(active, Does.Contain("north"));
            Assert.That(active, Does.Contain("east"));
            Assert.That(active, Does.Not.Contain("south"));
        }

        [Test]
        public void GetActiveNodeIds_IsSortedOrdinal()
        {
            var dim = MakeDimension("z-node", "a-node", "m-node");
            var combat = MakeCombat(dim);
            combat.CommitForce("z-node", "red", 1f);
            combat.CommitForce("a-node", "red", 1f);
            combat.CommitForce("m-node", "blue", 1f);

            var active = combat.GetActiveNodeIds();
            Assert.That(active[0], Is.EqualTo("a-node"));
            Assert.That(active[1], Is.EqualTo("m-node"));
            Assert.That(active[2], Is.EqualTo("z-node"));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Tick — attrition
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Tick_SingleFaction_NoAttrition()
        {
            // Solo faction at a node: nobody to fight, no attrition.
            // With decay=0 and propagation=0, logAmp stays constant.
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim, attrition: 0.5f, decay: 0f);
            combat.CommitForce("north", "red", 2f);

            combat.Tick(1f);

            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Tick_TwoFactions_BothTakeAttrition()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim, attrition: 0.5f, decay: 0f);
            combat.CommitForce("north", "red", 2f);
            combat.CommitForce("north", "blue", 1f);

            combat.Tick(1f);

            // red  takes: -blue(1.0)  × 0.5 × 1.0 = -0.5  → 1.5
            // blue takes: -red(2.0)   × 0.5 × 1.0 = -1.0  → 0.0 (field prunes near-zero)
            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.LessThan(2f));
            Assert.That(combat.Intensity.GetLogAmp("north", "blue"), Is.LessThan(1f));
            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.EqualTo(1.5f).Within(1e-4f));
        }

        [Test]
        public void Tick_AttritionProportionalToDt()
        {
            // Run one tick of dt=0.5 vs. one tick of dt=1.0 — result should differ proportionally.
            static float RunTick(float dt)
            {
                var dim = MakeDimension("n");
                var combat = MakeCombat(dim, attrition: 0.4f, decay: 0f);
                combat.CommitForce("n", "red", 3f);
                combat.CommitForce("n", "blue", 3f);
                combat.Tick(dt);
                return combat.Intensity.GetLogAmp("n", "red");
            }

            float resultHalfDt = RunTick(0.5f);
            float resultFullDt = RunTick(1.0f);

            // With dt=0.5: red loses 3.0 × 0.4 × 0.5 = 0.6 → 2.4
            // With dt=1.0: red loses 3.0 × 0.4 × 1.0 = 1.2 → 1.8
            Assert.That(resultHalfDt, Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(resultFullDt, Is.EqualTo(1.8f).Within(1e-4f));
        }

        [Test]
        public void Tick_ThreeFactions_EachAttritionByOtherTwo()
        {
            var dim = MakeDimension("hub");
            var combat = MakeCombat(dim, attrition: 0.2f, decay: 0f);
            combat.CommitForce("hub", "red", 3f);
            combat.CommitForce("hub", "blue", 2f);
            combat.CommitForce("hub", "green", 1f);

            combat.Tick(1f);

            // red   takes: -(blue+green) = -3.0 × 0.2 × 1.0 = -0.6 → 2.4
            // blue  takes: -(red+green)  = -4.0 × 0.2 × 1.0 = -0.8 → 1.2
            // green takes: -(red+blue)   = -5.0 × 0.2 × 1.0 = -1.0 → 0.0 (pruned)
            Assert.That(combat.Intensity.GetLogAmp("hub", "red"), Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(combat.Intensity.GetLogAmp("hub", "blue"), Is.EqualTo(1.2f).Within(1e-4f));
            Assert.That(combat.Intensity.GetLogAmp("hub", "green"),
                Is.LessThanOrEqualTo(0f).Or.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Tick_DoubleBuffered_AttritionReadsPreTickValues()
        {
            // If the buffer were single, applying red's attrition first would reduce red
            // before blue's attrition is computed, giving blue a smaller delta.
            // Double-buffered: both see the exact pre-tick state.
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim, attrition: 1.0f, decay: 0f);
            combat.CommitForce("north", "red", 2f);
            combat.CommitForce("north", "blue", 2f);

            combat.Tick(1f);

            // Both see opponent = 2.0 → both take -2.0 → both land at 0 (or near-zero)
            float redFinal = combat.Intensity.GetLogAmp("north", "red");
            float blueFinal = combat.Intensity.GetLogAmp("north", "blue");

            // With symmetric starting logAmps and high attrition,
            // double-buffer produces symmetric outcome.
            Assert.That(redFinal, Is.EqualTo(blueFinal).Within(1e-4f));
        }

        [Test]
        public void Tick_MultipleNodes_AttritionIndependent()
        {
            var dim = MakeDimension("north", "south");
            var combat = MakeCombat(dim, attrition: 0.5f, decay: 0f);
            // north: only red — no attrition
            combat.CommitForce("north", "red", 2f);
            // south: red vs blue — both take attrition
            combat.CommitForce("south", "red", 1f);
            combat.CommitForce("south", "blue", 1f);

            combat.Tick(1f);

            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(combat.Intensity.GetLogAmp("south", "red"), Is.LessThan(1f));
            Assert.That(combat.Intensity.GetLogAmp("south", "blue"), Is.LessThan(1f));
        }

        [Test]
        public void Tick_ZeroDt_NoAttritionNoDecay()
        {
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim, attrition: 99f, decay: 0.5f);
            combat.CommitForce("north", "red", 2f);
            combat.CommitForce("north", "blue", 2f);

            combat.Tick(0f);

            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(combat.Intensity.GetLogAmp("north", "blue"), Is.EqualTo(2f).Within(1e-4f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Determinism
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Tick_Determinism_SameStartSameResult()
        {
            static float[] RunSim()
            {
                var dim = MakeDimension("north", "south");
                var combat = MakeCombat(dim, attrition: 0.3f, decay: 0.05f);
                combat.CommitForce("north", "red", 2f);
                combat.CommitForce("north", "blue", 1.5f);
                combat.CommitForce("south", "red", 1f);
                combat.CommitForce("south", "green", 3f);

                for (int i = 0; i < 5; i++)
                    combat.Tick(0.25f);

                return new[]
                {
                    combat.Intensity.GetLogAmp("north", "red"),
                    combat.Intensity.GetLogAmp("north", "blue"),
                    combat.Intensity.GetLogAmp("south", "red"),
                    combat.Intensity.GetLogAmp("south", "green"),
                };
            }

            float[] a = RunSim();
            float[] b = RunSim();

            for (int i = 0; i < a.Length; i++)
                Assert.That(a[i], Is.EqualTo(b[i]).Within(1e-7f), $"Index {i} differs");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Multi-tick dynamics
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void MultiTick_StrongerFactionWins()
        {
            // With no reinforcement: weaker faction decays to zero faster
            // because it takes heavier attrition from the stronger opponent.
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim, attrition: 0.4f, decay: 0f);
            combat.CommitForce("north", "red", 4f);  // much stronger
            combat.CommitForce("north", "blue", 1f);  // weaker

            for (int i = 0; i < 5; i++)
                combat.Tick(1f);

            float redFinal = combat.Intensity.GetLogAmp("north", "red");
            float blueFinal = combat.Intensity.GetLogAmp("north", "blue");

            Assert.That(redFinal, Is.GreaterThan(blueFinal),
                "Stronger faction should retain more intensity than weaker one");
        }

        [Test]
        public void MultiTick_EqualFactions_MutualDecay()
        {
            // Equal forces: symmetric attrition halves each faction's logAmp every tick.
            //   aₙ₊₁ = aₙ - aₙ × 0.5 = 0.5 × aₙ  (geometric decay)
            // Starting at 1.0, LogEpsilon = 0.0001 → pruned after ~14 ticks (0.5¹⁴ ≈ 6e-5).
            var dim = MakeDimension("north");
            var combat = MakeCombat(dim, attrition: 0.5f, decay: 0f);
            combat.CommitForce("north", "red", 1f);
            combat.CommitForce("north", "blue", 1f);

            for (int i = 0; i < 15; i++)
                combat.Tick(1f);

            // Both below LogEpsilon → field pruned → no active nodes
            Assert.That(combat.GetActiveNodeIds().Count, Is.EqualTo(0));
        }

        [Test]
        public void MultiTick_NoActivity_IntensityDecaysViaFieldProfile()
        {
            // A lone faction at a node with decay=0.5: its logAmp should fall via Propagator.Step.
            var dim = MakeDimension("north");
            var profile = MakeProfile("combat.intensity", decay: 0.5f);
            var combat = new CombatSystem(dim, profile);
            combat.CommitForce("north", "red", 2f);

            combat.Tick(1f); // No opponent → no attrition, only decay
            combat.Tick(1f);

            Assert.That(combat.Intensity.GetLogAmp("north", "red"), Is.LessThan(2f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Integration: cross-system via CouplingRule
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Integration_CombatIntensity_DrivesWarExposureViaCouplingRule()
        {
            using var _ = default(System.IDisposable); // silence unused-var warning
            var dim = MakeDimension("north");

            var combatProfile = MakeProfile("combat.intensity", decay: 0f);
            var warProfile = new FieldProfile("war.demo")
            {
                LogEpsilon = 0.0001f,
                DecayRate = 0f,
                MinLogAmpClamp = 0f,
                MaxLogAmpClamp = 10f,
            };

            var combat = new CombatSystem(dim, combatProfile);
            var warField = dim.AddField("war.exposure", warProfile);

            // Commit an engagement
            combat.CommitForce("north", "red", 2f);
            combat.CommitForce("north", "blue", 1f);

            // Coupling: combat.intensity → war.exposure (channel "x")
            var rules = new List<Odengine.Coupling.CouplingRule>
            {
                new Odengine.Coupling.CouplingRule("combat.intensity", "war.exposure")
                {
                    InputChannelSelector  = "*",
                    OutputChannelSelector = "explicit:[x]",
                    Operator = Odengine.Coupling.CouplingOperator.Linear(0.3f),
                    ScaleByDeltaTime = true,
                }
            };

            combat.Tick(1f);
            Odengine.Coupling.CouplingProcessor.Step(dim, rules, deltaTime: 1f);

            // War exposure should have risen (red=2f + blue=1f → combined inject into "x")
            float warExposure = warField.GetLogAmp("north", "x");
            Assert.That(warExposure, Is.GreaterThan(0f),
                "Combat activity should drive war exposure up via coupling");
        }

        [Test]
        public void Integration_NoCombat_WarExposureUnchanged()
        {
            var dim = MakeDimension("north");
            var combatProfile = MakeProfile("combat.intensity", decay: 0f);
            var warProfile = new FieldProfile("war.demo")
            {
                LogEpsilon = 0.0001f,
                DecayRate = 0f,
                MinLogAmpClamp = 0f,
                MaxLogAmpClamp = 10f,
            };

            var combat = new CombatSystem(dim, combatProfile);
            var warField = dim.AddField("war.exposure", warProfile);
            warField.AddLogAmp("north", "x", 1f);

            var rules = new List<Odengine.Coupling.CouplingRule>
            {
                new Odengine.Coupling.CouplingRule("combat.intensity", "war.exposure")
                {
                    InputChannelSelector  = "*",
                    OutputChannelSelector = "explicit:[x]",
                    Operator = Odengine.Coupling.CouplingOperator.Linear(0.3f),
                    ScaleByDeltaTime = true,
                }
            };

            // No CommitForce — combat is empty
            combat.Tick(1f);
            Odengine.Coupling.CouplingProcessor.Step(dim, rules, deltaTime: 1f);

            Assert.That(warField.GetLogAmp("north", "x"), Is.EqualTo(1f).Within(1e-4f));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Invariants
        // ═══════════════════════════════════════════════════════════════════

        [Test]
        public void Tick_NoNaN_AfterManyTicks()
        {
            var dim = MakeDimension("a", "b", "c");
            var combat = MakeCombat(dim, attrition: 0.3f, decay: 0.05f);
            combat.CommitForce("a", "red", 3f);
            combat.CommitForce("a", "blue", 2f);
            combat.CommitForce("b", "green", 1f);
            combat.CommitForce("b", "red", 1.5f);

            for (int i = 0; i < 200; i++)
                combat.Tick(0.5f);

            foreach (var nodeId in new[] { "a", "b", "c" })
                foreach (var factionId in new[] { "red", "blue", "green" })
                {
                    float v = combat.Intensity.GetLogAmp(nodeId, factionId);
                    Assert.That(float.IsNaN(v), Is.False, $"NaN at ({nodeId},{factionId})");
                    Assert.That(float.IsInfinity(v), Is.False, $"Inf at ({nodeId},{factionId})");
                }
        }

        [Test]
        public void Tick_LogAmpsRespectProfileClamps()
        {
            var dim = MakeDimension("north");
            var profile = new FieldProfile("combat.intensity")
            {
                LogEpsilon = 0.0001f,
                MinLogAmpClamp = -5f,
                MaxLogAmpClamp = 5f,
                DecayRate = 0f,
                PropagationRate = 0f,
            };
            var combat = new CombatSystem(dim, profile, new CombatConfig { AttritionRate = 0.01f });
            combat.CommitForce("north", "red", 4.5f);
            combat.CommitForce("north", "blue", 4.5f);

            for (int i = 0; i < 10; i++)
                combat.Tick(1f);

            // Values should never exceed clamps
            Assert.That(combat.Intensity.GetLogAmp("north", "red"),
                Is.LessThanOrEqualTo(5f + 1e-4f));
            Assert.That(combat.Intensity.GetLogAmp("north", "red"),
                Is.GreaterThanOrEqualTo(-5f - 1e-4f));
        }
    }
}
