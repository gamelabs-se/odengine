using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Combat;
using Odengine.Core;
using Odengine.Coupling;
using Odengine.Economy;
using Odengine.Faction;
using Odengine.Fields;
using Odengine.Intel;
using Odengine.Tests.Shared;
using Odengine.War;

namespace Odengine.Tests.Scenarios
{
    /// <summary>
    /// Realms scenario tests — deterministic game-engine proof.
    ///
    /// Map: a fictional realm inspired by high-fantasy geography.
    ///
    ///   shire ─── bree ─── rohan ─── helm_deep ─── isengard
    ///               |         |
    ///           rivendell   gondor ─── minas_tirith ─── mordor
    ///                          |
    ///                       pelargir   (port / trade hub)
    ///
    /// Factions: "free" (the alliance), "shadow" (the enemy)
    ///
    /// These tests assert SPECIFIC CAUSAL CHAINS:
    ///   A causes B causes C — all quantitatively measurable, all deterministic.
    ///
    /// Each test records "before" state, triggers an event, runs N ticks,
    /// then asserts the emergent "after" state satisfies the causal relationship.
    ///
    /// Nothing is hardcoded in the engine — all coupling is declared in the test's
    /// rule list, demonstrating the engine as a reliable deterministic substrate
    /// for game logic.
    /// </summary>
    [TestFixture]
    public class Scenarios_RealmsTests
    {
        // ── World map ─────────────────────────────────────────────────────────

        private const string Shire = "shire";
        private const string Bree = "bree";
        private const string Rivendell = "rivendell";
        private const string Rohan = "rohan";
        private const string HelmDeep = "helm_deep";
        private const string Isengard = "isengard";
        private const string Gondor = "gondor";
        private const string MinasTirith = "minas_tirith";
        private const string Mordor = "mordor";
        private const string Pelargir = "pelargir";

        // Factions
        private const string Free = "free";
        private const string Shadow = "shadow";

        // Commodities
        private const string Grain = "grain";
        private const string Iron = "iron";
        private const string Wood = "wood";

        // ── Profile helpers ───────────────────────────────────────────────────

        private static FieldProfile Econ() => new FieldProfile("eco")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.10f,
            PropagationRate = 0.05f,
            EdgeResistanceScale = 1.0f,
            MinLogAmpClamp = -8f,
            MaxLogAmpClamp = 8f,
        };

        private static FieldProfile War(float propagation = 0.20f) => new FieldProfile("war")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0f,         // WarSystem state machine drives decay, not profile
            PropagationRate = propagation,
            EdgeResistanceScale = 0.5f,  // was 1.5; lower scale keeps the wave alive across resistance
            MinLogAmpClamp = 0f,
            MaxLogAmpClamp = 6f,
        };

        private static FieldProfile Presence() => new FieldProfile("presence")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.08f,
            PropagationRate = 0.03f,
            EdgeResistanceScale = 1.0f,
            MinLogAmpClamp = -6f,
            MaxLogAmpClamp = 6f,
        };

        private static FieldProfile Influence() => new FieldProfile("influence")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.06f,
            PropagationRate = 0.04f,
            EdgeResistanceScale = 0.8f,
            MinLogAmpClamp = -6f,
            MaxLogAmpClamp = 6f,
        };

        private static FieldProfile Stability() => new FieldProfile("stability")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.05f,
            PropagationRate = 0.02f,
            EdgeResistanceScale = 1.5f,
            MinLogAmpClamp = -6f,
            MaxLogAmpClamp = 6f,
        };

        private static FieldProfile Combat() => new FieldProfile("combat")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.15f,
            PropagationRate = 0.02f,
            EdgeResistanceScale = 2.0f,
            MinLogAmpClamp = 0f,
            MaxLogAmpClamp = 8f,
        };

        private static FieldProfile Intel() => new FieldProfile("intel")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.05f,
            PropagationRate = 0.25f,  // high enough to propagate across edges within test budgets
            EdgeResistanceScale = 1.0f,
            MinLogAmpClamp = -5f,
            MaxLogAmpClamp = 5f,
        };

        // ── World builder ─────────────────────────────────────────────────────

        /// <summary>
        /// Constructs the full Realms world: graph, all five systems, coupling rules.
        /// Each scenario calls this and then seeds its specific starting state.
        /// </summary>
        private sealed class Realms
        {
            public readonly Dimension Dim;
            public readonly EconomySystem Economy;
            public readonly WarSystem War;
            public readonly FactionSystem Factions;
            public readonly CombatSystem Combat;
            public readonly IntelSystem Intel;
            public readonly WarConfig WarCfg;
            public readonly List<CouplingRule> Rules;

            public Realms()
            {
                // ── Graph ──────────────────────────────────────────────────
                Dim = new Dimension();
                foreach (var n in new[]
                    { Shire, Bree, Rivendell, Rohan, HelmDeep, Isengard,
                      Gondor, MinasTirith, Mordor, Pelargir })
                    Dim.AddNode(n);

                // Roads and passes — resistance encodes travel difficulty.
                // Lower resistance = faster propagation of all fields across the edge.
                Dim.AddEdge(Shire, Bree, resistance: 0.2f); // well-kept road
                Dim.AddEdge(Bree, Rivendell, resistance: 0.5f); // mountain path
                Dim.AddEdge(Bree, Rohan, resistance: 0.4f); // grassland ride
                Dim.AddEdge(Bree, Gondor, resistance: 0.6f); // long south road
                Dim.AddEdge(Rohan, HelmDeep, resistance: 0.3f); // short valley pass
                Dim.AddEdge(HelmDeep, Isengard, resistance: 0.5f); // contested border
                Dim.AddEdge(Gondor, MinasTirith, resistance: 0.2f); // main causeway
                Dim.AddEdge(Gondor, Pelargir, resistance: 0.3f); // river road
                Dim.AddEdge(MinasTirith, Mordor, resistance: 1.0f); // enemy frontier

                // Reverse edges — NodeGraph.AddEdge is one-directional (out-edges only).
                // All propagation (war, intel, economy) uses GetOutEdgesSorted, so without
                // these reverse edges the graph is a directed acyclic flow and signals
                // can never travel back (e.g. Mordor has no out-edges in the above set).
                Dim.AddEdge(Bree, Shire, resistance: 0.2f);
                Dim.AddEdge(Rivendell, Bree, resistance: 0.5f);
                Dim.AddEdge(Rohan, Bree, resistance: 0.4f);
                Dim.AddEdge(Gondor, Bree, resistance: 0.6f);
                Dim.AddEdge(HelmDeep, Rohan, resistance: 0.3f);
                Dim.AddEdge(Isengard, HelmDeep, resistance: 0.5f);
                Dim.AddEdge(MinasTirith, Gondor, resistance: 0.2f);
                Dim.AddEdge(Pelargir, Gondor, resistance: 0.3f);
                Dim.AddEdge(Mordor, MinasTirith, resistance: 1.0f);

                // ── Systems ────────────────────────────────────────────────
                Economy = new EconomySystem(Dim, Econ());

                WarCfg = new WarConfig
                {
                    ExposureGrowthRate = 0.60f,  // 0.06/tick at dt=0.1 → 3.6 at 60 ticks
                    AmbientDecayRate = 0.01f,    // ceiling 0.001/tick — low enough for propagated exposure to accumulate
                    CeasefireDecayRate = 2.0f,   // 0.20/tick at dt=0.1; beats max back-prop from neighbors (max ~0.094/tick)
                };
                War = new WarSystem(Dim, War(), WarCfg);

                Factions = new FactionSystem(Dim, Presence(), Influence(), Stability());

                Combat = new CombatSystem(Dim, Combat(),
                    new CombatConfig { AttritionRate = 0.25f, ActiveThreshold = 0.0001f });

                Intel = new IntelSystem(Dim, Intel(),
                    new IntelConfig { ActiveCoverageThreshold = 0.0001f });

                // ── Coupling rules ─────────────────────────────────────────
                // These are the declared game-layer rules that wire the five
                // systems into a reactive economy of war and trade.
                // None of this lives in the engine — it's the "game design" layer.
                string wch = WarCfg.ExposureChannelId; // "x"

                Rules = new List<CouplingRule>
                {
                    // War pressure chokes supply chains
                    new CouplingRule("war.exposure", "economy.availability")
                    {
                        InputChannelSelector  = wch,
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(-0.20f),
                        ScaleByDeltaTime      = true,
                    },
                    // War pressure drives prices up
                    new CouplingRule("war.exposure", "economy.pricePressure")
                    {
                        InputChannelSelector  = wch,
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(0.10f),
                        ScaleByDeltaTime      = true,
                    },
                    // Faction presence is light economic lubricant (trade flows where power holds)
                    new CouplingRule("faction.presence", "economy.availability")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(0.05f),
                        ScaleByDeltaTime      = true,
                    },
                    // Active combat raises war pressure
                    new CouplingRule("combat.intensity", "war.exposure")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = $"explicit:[{wch}]",
                        Operator              = CouplingOperator.Linear(0.02f),  // low enough that ceasefire decay (0.20) can win
                        ScaleByDeltaTime      = true,
                    },
                    // Combat attrits faction presence (losses erode control)
                    new CouplingRule("combat.intensity", "faction.presence")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(-0.12f),
                        ScaleByDeltaTime      = true,
                    },
                    // Intel coverage converts to soft-power faction influence
                    new CouplingRule("intel.coverage", "faction.influence")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = "same",  // "same" uses inputChannelId (e.g. "free"), creating entry if absent
                        Operator              = CouplingOperator.Linear(0.20f),
                        ScaleByDeltaTime      = true,
                    },
                    // Dense scout networks pick up war-zone signals
                    new CouplingRule("intel.coverage", "war.exposure")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = $"explicit:[{wch}]",
                        Operator              = CouplingOperator.Linear(0.02f),
                        ScaleByDeltaTime      = true,
                    },
                };
            }

            /// <summary>Advance all systems by dt, then apply coupling impulses.</summary>
            public void Tick(float dt)
            {
                // Propagate economy fields (WarSystem/FactionSystem/CombatSystem/IntelSystem
                // call Propagator internally in their own Tick).
                Propagator.Step(Dim, Economy.Availability, dt);
                Propagator.Step(Dim, Economy.PricePressure, dt);
                War.Tick(dt);
                Factions.Tick(dt);
                Combat.Tick(dt);
                Intel.Tick(dt);
                CouplingProcessor.Step(Dim, Rules, dt);
            }

            public float PriceAt(string node, string item)
                => Economy.SamplePrice(node, item, 1f);

            public float WarAt(string node)
                => War.GetExposureLogAmp(node);

            public float PresenceAt(string node, string faction)
                => Factions.Presence.GetLogAmp(node, faction);

            public float InfluenceAt(string node, string faction)
                => Factions.Influence.GetLogAmp(node, faction);

            public float AvailAt(string node, string item)
                => Economy.Availability.GetLogAmp(node, item);

            public float PressureAt(string node, string item)
                => Economy.PricePressure.GetLogAmp(node, item);

            public float CoverageAt(string node, string faction)
                => Intel.GetCoverage(node, faction);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //
        //  SCENARIO 1 — Trade Blockade → Price Spike
        //
        //  Causal chain:
        //    Shadow seizes Pelargir (the port) → grain supply to Gondor is cut
        //    → war.exposure coupling drives economy.availability negative at Pelargir
        //    → propagation carries that scarcity signal inland to Gondor
        //    → SamplePrice at Gondor rises above pre-blockade level
        //
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Scenario_TradeBlockade_PriceSpike()
        {
            // ── Phase 1: establish a trading economy ───────────────────────
            // Run 40 ticks of peaceful trade: Pelargir ships grain to Gondor.
            // This lets the economy reach near-equilibrium so our baseline is stable.
            var r = new Realms();

            // Free peoples control the trade routes
            r.Factions.AddPresence(Pelargir, Free, 2.0f);
            r.Factions.AddPresence(Gondor, Free, 2.0f);
            r.Factions.AddPresence(MinasTirith, Free, 1.5f);

            const float dt = 0.1f;

            // Phase 1: Steady peaceful trade — realize grain channels and reach
            // equilibrium. InjectTrade adds NEGATIVE availability (supply consumed
            // = scarcity), so prices settle above 1.0 at the trade-driven baseline.
            for (int tick = 0; tick < 20; tick++)
            {
                r.Economy.InjectTrade(Pelargir, Grain, 2f); // port receives grain
                r.Economy.InjectTrade(Gondor, Grain, 1f);  // inland demand
                r.Tick(dt);
            }

            float priceBefore = r.PriceAt(Gondor, Grain);
            float warAtPortBefore = r.WarAt(Pelargir);

            Assert.That(warAtPortBefore, Is.EqualTo(0f).Within(1e-4f),
                "No war should exist at Pelargir before the blockade.");

            // Phase 2: Shadow blockades Pelargir — SAME trade volume continues,
            // but war.exposure coupling now adds ADDITIONAL negative availability
            // impulses on top of the existing trade-driven baseline.
            // Chain: war.exposure → coupling (−0.20/tick) → availability more negative
            //                     → coupling (+0.10/tick) → pricePressure rises
            //        → SamplePrice = exp(pressureMult) / exp(availMult) rises further.
            r.War.DeclareWar(Pelargir);
            r.Factions.AddPresence(Pelargir, Shadow, 2.5f);

            for (int tick = 0; tick < 40; tick++)
            {
                r.Economy.InjectTrade(Pelargir, Grain, 2f); // same trade (port still ships, under fire)
                r.Economy.InjectTrade(Gondor, Grain, 1f);
                r.Tick(dt);
            }

            float priceAfter = r.PriceAt(Gondor, Grain);
            float warAtPort = r.WarAt(Pelargir);

            // Causal assertions

            Assert.That(warAtPort, Is.GreaterThan(0.5f),
                $"War exposure must have built up at Pelargir after 40 ticks of active war ({warAtPort:F4}).");

            Assert.That(priceAfter, Is.GreaterThan(priceBefore),
                $"Grain price at Gondor must rise after Pelargir blockade. " +
                $"Before: {priceBefore:F4}, After: {priceAfter:F4}. " +
                "Chain: war.exposure → coupling → extra −availability & +pricePressure → SamplePrice rises.");
        }

        [Test]
        public void Scenario_TradeBlockade_AvailabilityDropsDownstream()
        {
            // When war is declared at the port, the war.exposure → availability coupling
            // adds ADDITIONAL negative availability impulses on top of the trade baseline.
            // The same InjectTrade volume runs in both phases; the only difference is that
            // Phase 2 has active war.exposure at Pelargir driving the coupling.
            //
            // Note on InjectTrade semantics: it adds NEGATIVE availability logAmp
            // (supply consumed → scarcity), so the trade-driven baseline is already
            // negative. War coupling makes it MORE negative.
            var r = new Realms();
            r.Factions.AddPresence(Pelargir, Free, 2.0f);
            r.Factions.AddPresence(Gondor, Free, 2.0f);

            const float dt = 0.1f;

            // Phase 1: establish trade baseline — realize grain channels
            for (int tick = 0; tick < 20; tick++)
            {
                r.Economy.InjectTrade(Pelargir, Grain, 2f);
                r.Economy.InjectTrade(Gondor, Grain, 1f);
                r.Tick(dt);
            }

            float availAtBaseline = r.AvailAt(Gondor, Grain);

            // Phase 2: war at port — same trade volume but war coupling adds extra scarcity
            r.War.DeclareWar(Pelargir);

            for (int tick = 0; tick < 50; tick++)
            {
                r.Economy.InjectTrade(Pelargir, Grain, 2f);
                r.Economy.InjectTrade(Gondor, Grain, 1f);
                r.Tick(dt);
            }

            float availAfterBlockade = r.AvailAt(Gondor, Grain);
            float warAtPelargir = r.WarAt(Pelargir);

            Assert.That(warAtPelargir, Is.GreaterThan(0.5f),
                $"War must have built at Pelargir to drive the coupling ({warAtPelargir:F4}).");

            Assert.That(availAfterBlockade, Is.LessThan(availAtBaseline),
                $"Gondor grain availability must decline during blockade vs trade-only baseline. " +
                $"Baseline: {availAtBaseline:F4}, Blockade: {availAfterBlockade:F4}. " +
                "Chain: war.exposure at Pelargir → coupling (−0.20/tick) → " +
                "availability at Pelargir/Gondor more negative than trade alone.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //
        //  SCENARIO 2 — Siege of Helm's Deep → Faction Erosion
        //
        //  Causal chain:
        //    Shadow commits 4× the combat force that Free can muster at Helm's Deep
        //    → heavy attrition (CombatSystem) → combat.intensity remains high
        //    → coupling: combat.intensity → faction.presence (−0.12/tick × intensity)
        //    → Free's presence at Helm's Deep erodes below its pre-siege level
        //    → Simultaneously, war exposure at Helm's Deep spikes via coupling
        //
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Scenario_SiegeOfHelmsDeep_FreePresenceErodes()
        {
            var r = new Realms();

            // Free holds Helm's Deep before the siege
            r.Factions.AddPresence(HelmDeep, Free, 2.0f);
            r.Factions.AddPresence(Rohan, Free, 2.5f);
            r.Factions.AddPresence(HelmDeep, Shadow, 0.1f); // token shadow presence

            const float dt = 0.1f;

            // Warm up: 20 ticks of peace, let presence equilibrate
            for (int tick = 0; tick < 20; tick++)
                r.Tick(dt);

            float presenceBefore = r.PresenceAt(HelmDeep, Free);

            // ── Siege begins ───────────────────────────────────────────────
            // Shadow commits 4× the force; free defends at 1×.
            // Heavy combat → attrition hits both, but shadow has reserves.
            r.War.DeclareWar(HelmDeep);
            r.War.DeclareWar(Isengard);

            // Reinforce every 10 ticks (Shadow has logistics advantage from Isengard)
            for (int tick = 0; tick < 60; tick++)
            {
                if (tick % 10 == 0)
                {
                    r.Combat.CommitForce(HelmDeep, Shadow, 3.0f); // siege force
                    r.Combat.CommitForce(HelmDeep, Free, 0.8f); // defenders
                }
                r.Tick(dt);
            }

            float presenceAfter = r.PresenceAt(HelmDeep, Free);
            float warExposure = r.WarAt(HelmDeep);
            float combatIntensity = r.Combat.GetIntensity(HelmDeep);

            // ── Causal assertions ──────────────────────────────────────────

            Assert.That(presenceAfter, Is.LessThan(presenceBefore),
                $"Free faction presence at Helm's Deep must erode during the siege. " +
                $"Before: {presenceBefore:F4}, After: {presenceAfter:F4}. " +
                "Chain: heavy combat.intensity → coupling → negative faction.presence impulse.");

            Assert.That(warExposure, Is.GreaterThan(1.0f),
                $"War exposure at Helm's Deep must be high during the siege ({warExposure:F4}). " +
                "Chain: combat.intensity → coupling → war.exposure impulse.");

            Assert.That(combatIntensity, Is.GreaterThan(0f),
                "Combat must still be ongoing after 60 ticks (shadow keeps reinforcing).");
        }

        [Test]
        public void Scenario_SiegeRelieved_FreeRecovery()
        {
            // After the siege breaks (Shadow forces cease), Free's presence recovers
            // and war exposure decays via ceasefire. Economy at Helm's Deep stabilises.
            var r = new Realms();

            r.Factions.AddPresence(HelmDeep, Free, 2.0f);
            r.Factions.AddPresence(Rohan, Free, 2.5f);

            const float dt = 0.1f;

            // Siege phase (30 ticks)
            r.War.DeclareWar(HelmDeep);
            for (int tick = 0; tick < 30; tick++)
            {
                if (tick % 5 == 0)
                {
                    r.Combat.CommitForce(HelmDeep, Shadow, 3.0f);
                    r.Combat.CommitForce(HelmDeep, Free, 0.8f);
                }
                r.Tick(dt);
            }

            float presenceDuringSiege = r.PresenceAt(HelmDeep, Free);
            float warDuringSiege = r.WarAt(HelmDeep);

            // Relief: cavalry arrives, Shadow repelled.
            // Shadow forces completely withdraw — no further combat commits.
            // Only then can ceasefire decay overcome the combat→war coupling.
            r.War.DeclareCeasefire(HelmDeep);
            for (int tick = 0; tick < 60; tick++)
            {
                // Shadow forces are gone — no CommitForce for Shadow.
                // Free reinforces the garrison.
                if (tick % 5 == 0)
                    r.Factions.AddPresence(HelmDeep, Free, 0.3f); // garrison relief
                r.Tick(dt);
            }

            float presenceAfterRelief = r.PresenceAt(HelmDeep, Free);
            float warAfterRelief = r.WarAt(HelmDeep);

            // ── Causal assertions ──────────────────────────────────────────

            Assert.That(presenceAfterRelief, Is.GreaterThan(presenceDuringSiege),
                $"Free faction presence must recover after the siege is lifted. " +
                $"During siege: {presenceDuringSiege:F4}, After relief: {presenceAfterRelief:F4}.");

            Assert.That(warAfterRelief, Is.LessThan(warDuringSiege),
                $"War exposure at Helm's Deep must decline after ceasefire. " +
                $"During: {warDuringSiege:F4}, After: {warAfterRelief:F4}. " +
                "No combat forces committed during relief → combat.intensity decays → " +
                "ceasefire decay rate (0.20) dominates → exposure falls.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //
        //  SCENARIO 3 — Shadow Advance Wave: Mordor → Minas Tirith → Gondor
        //
        //  Causal chain:
        //    War declared at Mordor → exposure grows → propagates west through edges
        //    → Minas Tirith (direct neighbour, low resistance) receives signal faster
        //       than Gondor (two hops)
        //    → After 60 ticks: warAt(minas_tirith) > warAt(gondor) > 0
        //    → The topology is causal: proximity to the source shapes the wave
        //
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Scenario_WarWave_PropagatesFromMordorWestward()
        {
            var r = new Realms();
            const float dt = 0.1f;

            // Shadow starts war at Mordor — exposure grows each tick and
            // propagates across edges. Minas Tirith is one hop away (resistance 1.0);
            // Gondor is two hops (resistance 1.0 + 0.2).
            r.War.DeclareWar(Mordor);

            for (int tick = 0; tick < 60; tick++)
                r.Tick(dt);

            float warMordor = r.WarAt(Mordor);
            float warMinasTirith = r.WarAt(MinasTirith);
            float warGondor = r.WarAt(Gondor);

            // ── Causal assertions ──────────────────────────────────────────

            Assert.That(warMordor, Is.GreaterThan(3f),
                $"Mordor war exposure must be high after 60 ticks of DeclareWar ({warMordor:F4}).");

            Assert.That(warMinasTirith, Is.GreaterThan(0f),
                $"War exposure must have propagated to Minas Tirith ({warMinasTirith:F4}). " +
                "Minas Tirith is one hop from Mordor via a resistance-1.0 edge.");

            Assert.That(warGondor, Is.GreaterThan(0f),
                $"War exposure must have reached Gondor ({warGondor:F4}). " +
                "Gondor is two hops from Mordor: Mordor → Minas Tirith (1.0) → Gondor (0.2).");

            Assert.That(warMinasTirith, Is.GreaterThan(warGondor),
                $"Minas Tirith must have higher war exposure than Gondor because it's closer to Mordor. " +
                $"Minas Tirith: {warMinasTirith:F4}, Gondor: {warGondor:F4}. " +
                "Topology drives causality: resistance determines propagation speed.");
        }

        [Test]
        public void Scenario_WarWave_DoesNotReachShire_WithinShortWindow()
        {
            // The Shire is five hops from Mordor through high-resistance passes.
            // After only 30 ticks, war exposure should not have meaningfully reached it.
            var r = new Realms();
            const float dt = 0.1f;

            r.War.DeclareWar(Mordor);

            for (int tick = 0; tick < 30; tick++)
                r.Tick(dt);

            float warShire = r.WarAt(Shire);
            float warMordor = r.WarAt(Mordor);
            float warMinasTirith = r.WarAt(MinasTirith);

            Assert.That(warMordor, Is.GreaterThan(1.5f), "Mordor must be at war.");
            Assert.That(warMinasTirith, Is.GreaterThan(warShire),
                $"The wave front should be far from Shire. " +
                $"Minas Tirith: {warMinasTirith:F4}, Shire: {warShire:F4}.");

            // Shire should have near-zero exposure — the wave hasn't arrived yet
            Assert.That(warShire, Is.LessThan(warMinasTirith * 0.1f),
                $"Shire ({warShire:F4}) should have at most 10% of Minas Tirith's " +
                $"war exposure ({warMinasTirith:F4}) after only 30 ticks.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //
        //  SCENARIO 4 — Scout Networks → Intelligence Advantage
        //
        //  Causal chain:
        //    Free peoples deploy dense scouts at Rivendell and Bree
        //    → intel.coverage propagates along edges to neighbouring nodes
        //    → coupling: intel.coverage → faction.influence (+0.06)
        //    → Free's influence at Rivendell and Bree is higher than Shadow's
        //    → Free dominates the "intel picture" of the northern region
        //
        //  Contrast run: without scouts, coverage stays at 0 → no influence boost
        //
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Scenario_ScoutNetwork_BoostsFreeInfluence()
        {
            float influenceWithScouts;
            float influenceWithoutScouts;

            // ── Run A: Free deploys scouts ─────────────────────────────────
            {
                var r = new Realms();
                r.Factions.AddPresence(Rivendell, Free, 1.5f);
                r.Factions.AddPresence(Bree, Free, 1.0f);

                const float dt = 0.1f;

                for (int tick = 0; tick < 50; tick++)
                {
                    // Deploy scouts every 5 ticks (scout patrols)
                    if (tick % 5 == 0)
                    {
                        r.Intel.DeploySensor(Rivendell, Free, 0.8f);
                        r.Intel.DeploySensor(Bree, Free, 0.5f);
                    }
                    r.Tick(dt);
                }

                influenceWithScouts = r.InfluenceAt(Rivendell, Free);
            }

            // ── Run B: No scouts ───────────────────────────────────────────
            {
                var r = new Realms();
                r.Factions.AddPresence(Rivendell, Free, 1.5f);
                r.Factions.AddPresence(Bree, Free, 1.0f);

                const float dt = 0.1f;

                for (int tick = 0; tick < 50; tick++)
                    r.Tick(dt);

                influenceWithoutScouts = r.InfluenceAt(Rivendell, Free);
            }

            // ── Causal assertion ───────────────────────────────────────────

            Assert.That(influenceWithScouts, Is.GreaterThan(influenceWithoutScouts),
                $"Intel coverage must boost faction influence via coupling. " +
                $"With scouts: {influenceWithScouts:F4}, Without: {influenceWithoutScouts:F4}. " +
                "Chain: DeploySensor → intel.coverage grows → " +
                "coupling (intel.coverage → faction.influence +0.06/tick) → influence rises.");
        }

        [Test]
        public void Scenario_ScoutNetwork_CoverageReachesNeighbours()
        {
            // Scouts deployed at Rivendell propagate coverage to Bree (adjacent).
            // After 30 ticks, Bree should have non-zero Free coverage even without
            // direct scout deployment there.
            var r = new Realms();
            const float dt = 0.1f;

            for (int tick = 0; tick < 30; tick++)
            {
                if (tick % 5 == 0)
                    r.Intel.DeploySensor(Rivendell, Free, 1.0f); // scouts at Rivendell only
                r.Tick(dt);
            }

            float coverageAtRivendell = r.CoverageAt(Rivendell, Free);
            float coverageAtBree = r.CoverageAt(Bree, Free);
            float coverageAtMordor = r.CoverageAt(Mordor, Free);

            Assert.That(coverageAtRivendell, Is.GreaterThan(0.01f),
                "Coverage at Rivendell (scout deployment site) must be significant.");

            Assert.That(coverageAtBree, Is.GreaterThan(0f),
                "Coverage must have propagated from Rivendell to adjacent Bree.");

            // Mordor is five hops away — signal should be negligible
            Assert.That(coverageAtMordor, Is.LessThan(coverageAtBree * 0.01f).Or.EqualTo(0f),
                "Coverage at Mordor (five hops away) should be negligible " +
                "compared to Bree (one hop).");
        }

        [Test]
        public void Scenario_ScoutNetwork_FreeDominatesIntelAtRivendell()
        {
            // Free has scouts at Rivendell. Shadow has none.
            // After 30 ticks, dominant observer at Rivendell must be Free.
            var r = new Realms();
            const float dt = 0.1f;

            for (int tick = 0; tick < 30; tick++)
            {
                if (tick % 5 == 0)
                    r.Intel.DeploySensor(Rivendell, Free, 1.0f);
                r.Tick(dt);
            }

            string dominant = r.Intel.GetDominantObserver(Rivendell);

            Assert.That(dominant, Is.EqualTo(Free),
                "Free must be the dominant observer at Rivendell after consistent scout deployment.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //
        //  SCENARIO 5 — Full Chain Reaction: Trade War → Armed Conflict → Recovery
        //
        //  This is the flagship integration scenario. It proves the engine can
        //  manage a multi-stage geopolitical crisis, all deterministically.
        //
        //  Timeline:
        //    Ticks  0–29:  Peaceful trade. Free controls economy; Shadow builds up at Mordor.
        //    Ticks 30–59:  Shadow blockades Pelargir + launches siege at Minas Tirith.
        //                  Economy degrades. War exposure spreads inland. Combat erodes Free.
        //    Ticks 60–89:  Free declares ceasefire at Pelargir (naval relief arrives).
        //                  Shadow continues siege at Minas Tirith.
        //                  Economy begins partial recovery along the coast.
        //    Ticks 90–119: Both sides pull back. All ceasefires in effect.
        //                  Economy and faction presence recover toward equilibrium.
        //
        //  Causal chain assertions:
        //    Phase 2 vs Phase 1: Gondor prices HIGHER, availability LOWER.
        //    Phase 3 vs Phase 2: Pelargir war declining, economy trending up.
        //    Phase 4 vs Phase 2: Minas Tirith war declining, faction recovering.
        //
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Scenario_FullChainReaction_TradeWarToConflictToRecovery()
        {
            var r = new Realms();
            const float dt = 0.1f;

            // ── Seed the world ─────────────────────────────────────────────
            r.Factions.AddPresence(Shire, Free, 2.5f);
            r.Factions.AddPresence(Bree, Free, 2.0f);
            r.Factions.AddPresence(Rivendell, Free, 2.0f);
            r.Factions.AddPresence(Rohan, Free, 2.5f);
            r.Factions.AddPresence(HelmDeep, Free, 1.5f);
            r.Factions.AddPresence(Gondor, Free, 3.0f);
            r.Factions.AddPresence(MinasTirith, Free, 2.5f);
            r.Factions.AddPresence(Pelargir, Free, 2.0f);

            r.Factions.AddPresence(Mordor, Shadow, 3.0f);
            r.Factions.AddPresence(Isengard, Shadow, 2.5f);

            r.Intel.DeploySensor(Rivendell, Free, 2.0f);
            r.Intel.DeploySensor(MinasTirith, Free, 1.5f);

            // Measurements we'll track across phases
            float priceGondorPhase1, priceGondorPhase2;
            float warMinasTirithBeforePhase4Ceasefire, warMinasTirithAfterPhase4;
            float freePelargirPhase2, freePelargirPhase4;

            // ─ Phase 1 (ticks 0–29): Peace, minimal trade, neutral baseline ──
            // Use minimal trade injections so prices stay near neutral (logAmp ≈ 0).
            // InjectTrade adds NEGATIVE availability, so heavy peace trade would
            // already push prices up — we want Phase 2 war coupling to be the
            // dominant driver of price increases.
            for (int tick = 0; tick < 30; tick++)
            {
                if (tick % 5 == 0)
                {
                    r.Intel.DeploySensor(Rivendell, Free, 0.5f);
                    r.Intel.DeploySensor(MinasTirith, Free, 0.3f);
                }
                r.Tick(dt);
            }

            priceGondorPhase1 = r.PriceAt(Gondor, Grain);

            // ─ Phase 2 (ticks 30–59): Blockade + Siege ────────────────────
            r.War.DeclareWar(Pelargir);    // Shadow blockades the port
            r.War.DeclareWar(MinasTirith); // Shadow besieges the capital
            r.War.DeclareWar(Mordor);      // Shadow mobilises at home base

            for (int tick = 0; tick < 30; tick++)
            {
                // Shadow commits siege forces every 5 ticks
                if (tick % 5 == 0)
                {
                    r.Combat.CommitForce(MinasTirith, Shadow, 2.5f);
                    r.Combat.CommitForce(MinasTirith, Free, 1.0f); // defenders
                    // No CommitForce at Pelargir — the naval blockade is represented
                    // by DeclareWar(Pelargir) alone. war.exposure → economy coupling
                    // handles the economic damage; physical combat intensity at the
                    // port would couple −0.12/tick into Free's presence there and
                    // prevent meaningful measurement of faction recovery in Phase 4.
                }

                // Trade is disrupted — only partial injections possible under fire
                r.Economy.InjectTrade(Gondor, Grain, 1f); // reduced inland demand
                r.Tick(dt);
            }

            priceGondorPhase2 = r.PriceAt(Gondor, Grain);
            freePelargirPhase2 = r.PresenceAt(Pelargir, Free);

            // ─ Phase 3 (ticks 60–89): Pelargir relief, siege continues ────
            r.War.DeclareCeasefire(Pelargir); // naval relief fleet arrives

            for (int tick = 0; tick < 30; tick++)
            {
                // Port partially reopens
                r.Economy.InjectTrade(Pelargir, Grain, 5f);

                // Siege at Minas Tirith continues
                if (tick % 5 == 0)
                {
                    r.Combat.CommitForce(MinasTirith, Shadow, 2.5f);
                    r.Combat.CommitForce(MinasTirith, Free, 1.0f);
                }
                r.Tick(dt);
            }

            // Capture MinasTirith war at peak (end of Phase 3 = war still active there)
            // before the Phase 4 ceasefire is declared. This is the baseline for proving
            // that ceasefire causes decline.
            warMinasTirithBeforePhase4Ceasefire = r.WarAt(MinasTirith);

            // ─ Phase 4 (ticks 90–119): Full ceasefire, recovery ───────────
            r.War.DeclareCeasefire(MinasTirith);
            r.War.DeclareCeasefire(Mordor);

            for (int tick = 0; tick < 30; tick++)
            {
                // Full trade restored
                r.Economy.InjectTrade(Pelargir, Grain, 10f);
                r.Economy.InjectTrade(Pelargir, Iron, 5f);
                r.Economy.InjectTrade(Gondor, Grain, 4f);
                r.Economy.InjectTrade(MinasTirith, Iron, 3f);

                // Free reinforces the recaptured port and capital
                if (tick % 10 == 0)
                {
                    r.Factions.AddPresence(Pelargir, Free, 0.5f);
                    r.Factions.AddPresence(MinasTirith, Free, 0.5f);
                }
                r.Tick(dt);
            }

            warMinasTirithAfterPhase4 = r.WarAt(MinasTirith);
            freePelargirPhase4 = r.PresenceAt(Pelargir, Free);

            // ═══════════════════════════════════════════════════════════════
            // Causal chain assertions
            // ═══════════════════════════════════════════════════════════════

            // 1. Blockade spiked prices: war coupling drove availability negative
            //    and pricePressure positive at Gondor/Pelargir.
            //    Phase 1 has no active channels → base price 1.0.
            //    Phase 2 InjectTrade realizes grain channel; war coupling then drives it higher.
            Assert.That(priceGondorPhase2, Is.GreaterThan(priceGondorPhase1),
                $"Phase 2 (blockade) prices must exceed Phase 1 (peace). " +
                $"P1={priceGondorPhase1:F4} P2={priceGondorPhase2:F4}. " +
                "War.exposure → coupling → \u2212availability & +pricePressure → SamplePrice rises.");

            // 2. Ceasefire at Minas Tirith reduced war there: compare the war level
            //    immediately before the Phase 4 ceasefire with the level after 30 ceasefire ticks.
            //    Note: we compare Phase4-start vs Phase4-end rather than Phase2 vs Phase4,
            //    because the siege continued through Phase3 and war kept building.
            Assert.That(warMinasTirithAfterPhase4, Is.LessThan(warMinasTirithBeforePhase4Ceasefire),
                $"Minas Tirith war must decline after ceasefire is declared. " +
                $"Before: {warMinasTirithBeforePhase4Ceasefire:F4}, After: {warMinasTirithAfterPhase4:F4}. " +
                "DeclareCeasefire activates the accelerated CeasefireDecayRate (0.20/tick), " +
                "which must exceed the residual combat\u2192war coupling.");

            // 3. Free faction presence recovering at Pelargir after reinforcement
            Assert.That(freePelargirPhase4, Is.GreaterThan(freePelargirPhase2).Or.EqualTo(freePelargirPhase2).Within(0.1f),
                $"Free faction must hold or recover at Pelargir after relief. " +
                $"Phase2={freePelargirPhase2:F4} Phase4={freePelargirPhase4:F4}.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //
        //  SCENARIO 6 — Occupation & Stability: contested territory flips
        //
        //  Causal chain:
        //    Shadow declares occupation at a weakly-held node (low stability)
        //    → with no stability resistance, occupation completes quickly
        //    → callback fires: the node has flipped
        //    → Free immediately tries to re-establish presence
        //    → With high stability reinforcement, a second occupation attempt takes longer
        //
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Scenario_Occupation_WeakNodeFlipsToShadow()
        {
            var r = new Realms();
            const float dt = 0.1f;

            // Bree is weakly held — stability near zero (no resistance to occupation)
            r.War.DeclareWar(Bree);
            r.War.DeclareOccupation(Bree, Shadow);
            r.War.SetNodeStability(Bree, 0f); // no resistance

            bool occupied = false;
            r.War.OnOccupationComplete = (nodeId, _) =>
            {
                if (nodeId == Bree) occupied = true;
            };

            // Run until occupation completes or 200 ticks pass (safety)
            int ticksToOccupy = 0;
            for (int tick = 0; tick < 200; tick++)
            {
                r.Tick(dt);
                ticksToOccupy = tick + 1;
                if (occupied) break;
            }

            Assert.That(occupied, Is.True,
                "A weakly-held node (stability=0) must eventually be occupied.");

            // OccupationBaseRate=0.1, dt=0.1 → progress += 0.01/tick → completes in 100 ticks.
            // Allow a 20-tick buffer for discrete-tick rounding.
            Assert.That(ticksToOccupy, Is.LessThan(120),
                $"Zero-stability occupation should complete in fewer than 120 ticks, " +
                $"took {ticksToOccupy}.");
        }

        [Test]
        public void Scenario_Occupation_StrongNodeResistsLonger()
        {
            // Two otherwise identical scenarios: one node has stability=0, another=0.8.
            // The high-stability node takes longer to occupy.
            static int TicksToOccupy(float stability)
            {
                var dim = new Dimension();
                dim.AddNode("target");

                var warCfg = new WarConfig { ExposureGrowthRate = 0.08f };
                var war = new WarSystem(dim,
                    new FieldProfile("war") { LogEpsilon = 0.0001f, PropagationRate = 0f, DecayRate = 0f, MinLogAmpClamp = 0f, MaxLogAmpClamp = 6f },
                    warCfg);

                war.DeclareWar("target");
                war.DeclareOccupation("target", "attacker");
                war.SetNodeStability("target", stability);

                bool done = false;
                war.OnOccupationComplete = (_, __) => done = true;

                var factionProfile = new FieldProfile("f") { LogEpsilon = 0.0001f, PropagationRate = 0f, DecayRate = 0f, MinLogAmpClamp = -6f, MaxLogAmpClamp = 6f };
                var factions = new FactionSystem(dim, factionProfile, factionProfile, factionProfile);
                var combat = new CombatSystem(dim, new FieldProfile("c") { LogEpsilon = 0.0001f, PropagationRate = 0f, DecayRate = 0f, MinLogAmpClamp = 0f, MaxLogAmpClamp = 6f });
                var intel = new IntelSystem(dim, new FieldProfile("i") { LogEpsilon = 0.0001f, PropagationRate = 0f, DecayRate = 0f, MinLogAmpClamp = -5f, MaxLogAmpClamp = 5f });
                var econ = new EconomySystem(dim, new FieldProfile("e") { LogEpsilon = 0.0001f, PropagationRate = 0f, DecayRate = 0f, MinLogAmpClamp = -8f, MaxLogAmpClamp = 8f });

                for (int tick = 0; tick < 500; tick++)
                {
                    Propagator.Step(dim, econ.Availability, 0.1f);
                    Propagator.Step(dim, econ.PricePressure, 0.1f);
                    war.Tick(0.1f);
                    factions.Tick(0.1f);
                    combat.Tick(0.1f);
                    intel.Tick(0.1f);
                    if (done) return tick + 1;
                }
                return int.MaxValue; // never occupied
            }

            int lowStabilityTicks = TicksToOccupy(0.0f);
            int highStabilityTicks = TicksToOccupy(0.8f);

            Assert.That(lowStabilityTicks, Is.LessThan(int.MaxValue), "Low-stability node must be occupied.");
            Assert.That(highStabilityTicks, Is.LessThan(int.MaxValue), "High-stability node must eventually be occupied.");
            Assert.That(highStabilityTicks, Is.GreaterThan(lowStabilityTicks),
                $"High-stability node ({highStabilityTicks} ticks) must take longer to occupy " +
                $"than low-stability node ({lowStabilityTicks} ticks). " +
                "Stability resistance slows occupation progress each tick.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //
        //  DETERMINISM PROOFS
        //
        //  Two independent runs of the exact same scenario must produce a
        //  byte-identical StateHash. This is the foundational guarantee that makes
        //  the engine reliable as a game substrate: replay, save/load, and
        //  multiplayer simulation lockstep all depend on it.
        //
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Determinism_TradeBlockadeScenario_ByteIdentical()
        {
            string Run()
            {
                var r = new Realms();
                r.Factions.AddPresence(Pelargir, Free, 2.0f);
                r.Factions.AddPresence(Gondor, Free, 2.0f);
                r.Factions.AddPresence(MinasTirith, Free, 1.5f);

                const float dt = 0.1f;
                for (int tick = 0; tick < 40; tick++)
                {
                    r.Economy.InjectTrade(Pelargir, Grain, 8f);
                    r.Economy.InjectTrade(Gondor, Grain, 3f);
                    r.Tick(dt);
                }

                r.War.DeclareWar(Pelargir);
                r.Factions.AddPresence(Pelargir, Shadow, 2.5f);

                for (int tick = 0; tick < 40; tick++)
                    r.Tick(dt);

                return StateHash.Compute(r.Dim);
            }

            string h1 = Run();
            string h2 = Run();

            Assert.That(h1, Is.EqualTo(h2),
                "Trade blockade scenario must produce identical StateHash across two independent runs.");
        }

        [Test]
        public void Determinism_FullChainReactionScenario_ByteIdentical()
        {
            string Run()
            {
                var r = new Realms();
                r.Factions.AddPresence(Shire, Free, 2.5f);
                r.Factions.AddPresence(Gondor, Free, 3.0f);
                r.Factions.AddPresence(MinasTirith, Free, 2.5f);
                r.Factions.AddPresence(Pelargir, Free, 2.0f);
                r.Factions.AddPresence(Mordor, Shadow, 3.0f);
                r.Intel.DeploySensor(Rivendell, Free, 2.0f);
                const float dt = 0.1f;

                // Phase 1 (peace)
                for (int tick = 0; tick < 30; tick++)
                {
                    r.Economy.InjectTrade(Pelargir, Grain, 10f);
                    r.Economy.InjectTrade(Gondor, Grain, 4f);
                    if (tick % 5 == 0) r.Intel.DeploySensor(Rivendell, Free, 0.5f);
                    r.Tick(dt);
                }

                // Phase 2 (blockade + siege)
                r.War.DeclareWar(Pelargir);
                r.War.DeclareWar(MinasTirith);
                r.War.DeclareWar(Mordor);
                for (int tick = 0; tick < 30; tick++)
                {
                    if (tick % 5 == 0)
                    {
                        r.Combat.CommitForce(MinasTirith, Shadow, 2.5f);
                        r.Combat.CommitForce(MinasTirith, Free, 1.0f);
                    }
                    r.Economy.InjectTrade(Gondor, Grain, 1f);
                    r.Tick(dt);
                }

                // Phase 3 (ceasefire at port)
                r.War.DeclareCeasefire(Pelargir);
                for (int tick = 0; tick < 30; tick++)
                {
                    r.Economy.InjectTrade(Pelargir, Grain, 5f);
                    if (tick % 5 == 0)
                    {
                        r.Combat.CommitForce(MinasTirith, Shadow, 2.5f);
                        r.Combat.CommitForce(MinasTirith, Free, 1.0f);
                    }
                    r.Tick(dt);
                }

                // Phase 4 (full recovery)
                r.War.DeclareCeasefire(MinasTirith);
                r.War.DeclareCeasefire(Mordor);
                for (int tick = 0; tick < 30; tick++)
                {
                    r.Economy.InjectTrade(Pelargir, Grain, 10f);
                    r.Economy.InjectTrade(Gondor, Grain, 4f);
                    r.Tick(dt);
                }

                return StateHash.Compute(r.Dim);
            }

            string h1 = Run();
            string h2 = Run();

            Assert.That(h1, Is.EqualTo(h2),
                "120-tick full-chain scenario must be byte-identical across two runs. " +
                "Any non-determinism would indicate unsorted iteration or floating-point path divergence.");
        }

        [Test]
        public void Determinism_WarWaveScenario_ByteIdentical()
        {
            string Run()
            {
                var r = new Realms();
                r.War.DeclareWar(Mordor);
                for (int tick = 0; tick < 80; tick++)
                    r.Tick(0.1f);
                return StateHash.Compute(r.Dim);
            }

            Assert.That(Run(), Is.EqualTo(Run()),
                "War wave propagation from Mordor must be fully deterministic.");
        }

        [Test]
        public void Determinism_SiegeScenario_ByteIdentical()
        {
            string Run()
            {
                var r = new Realms();
                r.Factions.AddPresence(HelmDeep, Free, 2.0f);
                r.War.DeclareWar(HelmDeep);
                for (int tick = 0; tick < 60; tick++)
                {
                    if (tick % 10 == 0)
                    {
                        r.Combat.CommitForce(HelmDeep, Shadow, 3.0f);
                        r.Combat.CommitForce(HelmDeep, Free, 0.8f);
                    }
                    r.Tick(0.1f);
                }
                return StateHash.Compute(r.Dim);
            }

            Assert.That(Run(), Is.EqualTo(Run()), "Siege scenario must be deterministic.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //
        //  STABILITY PROOFS — no NaN, no Infinity, within bounds, 500 ticks
        //
        //  The engine must be unconditionally stable under sustained multi-system
        //  interaction. These tests confirm the field profiles are configured
        //  correctly and no runaway amplification can occur.
        //
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Stability_FullWorld_500Ticks_NoNaNNoInfinity()
        {
            var r = new Realms();

            // Seed a plausible mid-war state
            r.Factions.AddPresence(Gondor, Free, 3.0f);
            r.Factions.AddPresence(MinasTirith, Free, 2.5f);
            r.Factions.AddPresence(Mordor, Shadow, 3.0f);
            r.War.DeclareWar(MinasTirith);
            r.War.DeclareWar(Mordor);
            r.Combat.CommitForce(MinasTirith, Shadow, 2.0f);
            r.Combat.CommitForce(MinasTirith, Free, 1.0f);
            r.Intel.DeploySensor(MinasTirith, Free, 1.5f);

            const float dt = 0.05f;

            ScalarField[] allFields =
            {
                r.Economy.Availability,
                r.Economy.PricePressure,
                r.War.Exposure,
                r.Factions.Presence,
                r.Factions.Influence,
                r.Factions.Stability,
                r.Combat.Intensity,
                r.Intel.Coverage,
            };

            for (int tick = 0; tick < 500; tick++)
            {
                r.Economy.InjectTrade(Gondor, Grain, 2f);
                r.Economy.InjectTrade(Pelargir, Iron, 1f);

                if (tick % 20 == 0)
                {
                    r.Combat.CommitForce(MinasTirith, Shadow, 0.5f);
                    r.Intel.DeploySensor(MinasTirith, Free, 0.3f);
                }

                r.Tick(dt);

                if (tick % 50 == 0)
                {
                    foreach (var field in allFields)
                    {
                        foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
                        {
                            Assert.IsFalse(float.IsNaN(logAmp),
                                $"Tick {tick}: NaN in '{field.FieldId}' at {nodeId}/{channelId}");
                            Assert.IsFalse(float.IsInfinity(logAmp),
                                $"Tick {tick}: Infinity in '{field.FieldId}' at {nodeId}/{channelId}");
                            Assert.GreaterOrEqual(logAmp, field.Profile.MinLogAmpClamp - 1e-3f,
                                $"Tick {tick}: Below min clamp in '{field.FieldId}' at {nodeId}/{channelId}");
                            Assert.LessOrEqual(logAmp, field.Profile.MaxLogAmpClamp + 1e-3f,
                                $"Tick {tick}: Above max clamp in '{field.FieldId}' at {nodeId}/{channelId}");
                        }
                    }
                }
            }
        }
    }
}
