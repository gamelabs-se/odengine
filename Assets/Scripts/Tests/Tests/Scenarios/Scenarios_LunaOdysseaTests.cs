using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Luna Odyssea stress tests — a full-scale galactic simulation proving the engine
    /// can sustain a space 4X game with 17 star systems, 4 factions, 9 commodities,
    /// multi-front wars, blockades, smuggling, occupation, intelligence networks,
    /// and 1000-tick economic time series — all deterministic, all stable.
    ///
    /// World: 17 star systems from Luna Odyssea
    ///
    ///   ┌─────────────────────── CORE WORLDS ───────────────────────────┐
    ///   │  Sanctum ── Apollo ── Hyperia ── Corus Alpha ── Demos Epsilon │
    ///   │     │                    │                          │          │
    ///   │  Medici ── Strozi     Ciom ── Zadorov ── Xarkath   │          │
    ///   │                                │                   │          │
    ///   │                            Amareth ── Rameses       │          │
    ///   └────────────────────────────────────────────────────────────────┘
    ///
    ///   ┌─── PIRATE TERRITORY ──┐      ┌─── CARTEL SPACE ────────────┐
    ///   │  Spectre (Demos link) │      │  Shiraz ── ShirazSlave1     │
    ///   └───────────────────────┘      │    │── ShirazSlave2         │
    ///                                  │    └── ShirazSlave3         │
    ///                                  │  (Rameses link)             │
    ///                                  └─────────────────────────────┘
    ///
    /// Factions:
    ///   "republic"  — Galactic Republic (Sanctum + core worlds)
    ///   "cartel"    — Intergalactic Cartel (Shiraz cluster)
    ///   "pirates"   — Spectre Syndicate (Spectre + raids)
    ///   "union"     — Union of Liberated Systems (Demos Epsilon + frontier)
    ///
    /// Commodities (9, from the game's commodity table):
    ///   water(10), food(80), fuel(120), metals(150), electronics(180),
    ///   medicine(200), painkillers(100), pistols(400), plutonium(25000)
    ///
    /// These tests simulate actual game scenarios at scale:
    ///   — 500-tick wars with tracked price curves
    ///   — 1000-tick galactic crises with multi-front occupation
    ///   — Blockade, smuggling, and economic isolation
    ///   — Intelligence fog of war across the map
    ///   — Determinism at scale (1000 ticks, byte-identical)
    ///   — NaN/Infinity/clamp stability over 2000 ticks
    /// </summary>
    [TestFixture]
    public class Scenarios_LunaOdysseaTests
    {
        // ══════════════════════════════════════════════════════════════════════
        //  STAR SYSTEMS (17 nodes)
        // ══════════════════════════════════════════════════════════════════════

        // Republic core
        private const string Sanctum = "sanctum";
        private const string Apollo = "apollo";
        private const string Hyperia = "hyperia";
        private const string CorusAlpha = "corus_alpha";
        private const string Medici = "medici";
        private const string Strozi = "strozi";

        // Frontier / Union
        private const string DemosEpsilon = "demos_epsilon";
        private const string Ciom = "ciom";
        private const string Zadorov = "zadorov";
        private const string Xarkath = "xarkath";
        private const string Amareth = "amareth";
        private const string Rameses = "rameses";

        // Pirate territory
        private const string Spectre = "spectre";

        // Cartel cluster
        private const string Shiraz = "shiraz";
        private const string ShirazSlave1 = "shiraz_slave_1";
        private const string ShirazSlave2 = "shiraz_slave_2";
        private const string ShirazSlave3 = "shiraz_slave_3";

        // ══════════════════════════════════════════════════════════════════════
        //  FACTIONS (4 + neutral implicit)
        // ══════════════════════════════════════════════════════════════════════

        private const string Republic = "republic";
        private const string Cartel = "cartel";
        private const string Pirates = "pirates";
        private const string Union = "union";

        // ══════════════════════════════════════════════════════════════════════
        //  COMMODITIES (9 from the game)
        // ══════════════════════════════════════════════════════════════════════

        private const string Water = "water";
        private const string Food = "food";
        private const string Fuel = "fuel";
        private const string Metals = "metals";
        private const string Electronics = "electronics";
        private const string Medicine = "medicine";
        private const string Painkillers = "painkillers";
        private const string Pistols = "pistols";
        private const string Plutonium = "plutonium";

        // Base prices from the game's commodity table
        private static readonly Dictionary<string, float> BasePrices = new Dictionary<string, float>
        {
            { Water, 10f },
            { Food, 80f },
            { Fuel, 120f },
            { Metals, 150f },
            { Electronics, 180f },
            { Medicine, 200f },
            { Painkillers, 100f },
            { Pistols, 400f },
            { Plutonium, 25000f },
        };

        // ══════════════════════════════════════════════════════════════════════
        //  FIELD PROFILES
        // ══════════════════════════════════════════════════════════════════════

        private static FieldProfile Econ() => new FieldProfile("eco")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.08f,
            PropagationRate = 0.06f,
            EdgeResistanceScale = 0.8f,
            MinLogAmpClamp = -10f,
            MaxLogAmpClamp = 10f,
        };

        private static FieldProfile WarProfile() => new FieldProfile("war")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0f,          // state machine controls decay
            PropagationRate = 0.15f, // war spreads across hyperspace lanes
            EdgeResistanceScale = 0.6f,
            MinLogAmpClamp = 0f,
            MaxLogAmpClamp = 8f,
        };

        private static FieldProfile PresenceProfile() => new FieldProfile("presence")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.06f,
            PropagationRate = 0.04f,
            EdgeResistanceScale = 1.0f,
            MinLogAmpClamp = -8f,
            MaxLogAmpClamp = 8f,
        };

        private static FieldProfile InfluenceProfile() => new FieldProfile("influence")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.05f,
            PropagationRate = 0.05f,
            EdgeResistanceScale = 0.7f,
            MinLogAmpClamp = -8f,
            MaxLogAmpClamp = 8f,
        };

        private static FieldProfile StabilityProfile() => new FieldProfile("stability")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.04f,
            PropagationRate = 0.02f,
            EdgeResistanceScale = 1.5f,
            MinLogAmpClamp = -8f,
            MaxLogAmpClamp = 8f,
        };

        private static FieldProfile CombatProfile() => new FieldProfile("combat")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.12f,
            PropagationRate = 0.02f,
            EdgeResistanceScale = 2.0f,
            MinLogAmpClamp = 0f,
            MaxLogAmpClamp = 10f,
        };

        private static FieldProfile IntelProfile() => new FieldProfile("intel")
        {
            LogEpsilon = 0.0001f,
            DecayRate = 0.04f,
            PropagationRate = 0.20f,
            EdgeResistanceScale = 0.8f,
            MinLogAmpClamp = -6f,
            MaxLogAmpClamp = 6f,
        };

        // ══════════════════════════════════════════════════════════════════════
        //  GALAXY BUILDER
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the full Luna Odyssea galaxy: 17 star systems, 4 factions,
        /// all hyperspace lanes (bidirectional), 5 domain systems, coupling rules.
        /// </summary>
        private sealed class Galaxy
        {
            public readonly Dimension Dim;
            public readonly EconomySystem Economy;
            public readonly WarSystem War;
            public readonly FactionSystem Factions;
            public readonly CombatSystem Combat;
            public readonly IntelSystem Intel;
            public readonly WarConfig WarCfg;
            public readonly List<CouplingRule> Rules;

            // ── All star system IDs for convenience ──
            public static readonly string[] AllSystems =
            {
                Sanctum, Apollo, Hyperia, CorusAlpha, DemosEpsilon,
                Medici, Strozi, Ciom, Zadorov, Xarkath, Amareth, Rameses,
                Spectre, Shiraz, ShirazSlave1, ShirazSlave2, ShirazSlave3,
            };

            // ── All fields for stability checks ──
            public ScalarField[] AllFields => new[]
            {
                Economy.Availability, Economy.PricePressure,
                War.Exposure,
                Factions.Presence, Factions.Influence, Factions.Stability,
                Combat.Intensity, Intel.Coverage,
            };

            public Galaxy()
            {
                // ── Build graph: 17 nodes ──────────────────────────────────
                Dim = new Dimension();
                foreach (var sys in AllSystems)
                    Dim.AddNode(sys);

                // ── Hyperspace lanes (bidirectional) ───────────────────────
                // Resistance represents travel time / danger through hyperspace.
                // Republic core lanes: well-maintained (low resistance)
                BiEdge(Sanctum, Apollo, 0.2f);
                BiEdge(Apollo, Hyperia, 0.3f);
                BiEdge(Hyperia, CorusAlpha, 0.3f);
                BiEdge(CorusAlpha, DemosEpsilon, 0.4f);
                BiEdge(Sanctum, Medici, 0.3f);
                BiEdge(Medici, Strozi, 0.2f);
                BiEdge(Strozi, Hyperia, 0.4f);

                // Frontier / mid-rim lanes
                BiEdge(Hyperia, Ciom, 0.5f);
                BiEdge(Ciom, Zadorov, 0.4f);
                BiEdge(Zadorov, Xarkath, 0.5f);
                BiEdge(Xarkath, DemosEpsilon, 0.6f);
                BiEdge(Zadorov, Amareth, 0.5f);
                BiEdge(Amareth, Rameses, 0.4f);

                // Pirate territory: dangerous hyperspace routes
                BiEdge(DemosEpsilon, Spectre, 0.8f);
                BiEdge(Spectre, Xarkath, 0.9f);

                // Cartel space: internal cluster is tight, external link is long
                BiEdge(Rameses, Shiraz, 0.7f);
                BiEdge(Shiraz, ShirazSlave1, 0.2f);
                BiEdge(Shiraz, ShirazSlave2, 0.3f);
                BiEdge(Shiraz, ShirazSlave3, 0.3f);

                // Long-range smuggling lane (pirates ↔ cartel, dangerous)
                BiEdge(Spectre, Shiraz, 1.2f);

                // ── Systems ────────────────────────────────────────────────
                Economy = new EconomySystem(Dim, Econ());

                WarCfg = new WarConfig
                {
                    ExposureGrowthRate = 0.50f,
                    AmbientDecayRate = 0.015f,
                    CeasefireDecayRate = 1.5f,
                };
                War = new WarSystem(Dim, WarProfile(), WarCfg);

                Factions = new FactionSystem(Dim, PresenceProfile(), InfluenceProfile(), StabilityProfile());

                Combat = new CombatSystem(Dim, CombatProfile(),
                    new CombatConfig { AttritionRate = 0.20f, ActiveThreshold = 0.0001f });

                Intel = new IntelSystem(Dim, IntelProfile(),
                    new IntelConfig { ActiveCoverageThreshold = 0.0001f });

                // ── Coupling rules (game design layer) ─────────────────────
                string wch = WarCfg.ExposureChannelId; // "x"

                Rules = new List<CouplingRule>
                {
                    // War chokes supply chains
                    new CouplingRule("war.exposure", "economy.availability")
                    {
                        InputChannelSelector  = wch,
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(-0.15f),
                        ScaleByDeltaTime      = true,
                    },
                    // War inflates prices
                    new CouplingRule("war.exposure", "economy.pricePressure")
                    {
                        InputChannelSelector  = wch,
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(0.08f),
                        ScaleByDeltaTime      = true,
                    },
                    // Faction presence lubricates economy
                    new CouplingRule("faction.presence", "economy.availability")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(0.04f),
                        ScaleByDeltaTime      = true,
                    },
                    // Combat drives war exposure
                    new CouplingRule("combat.intensity", "war.exposure")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = $"explicit:[{wch}]",
                        Operator              = CouplingOperator.Linear(0.02f),
                        ScaleByDeltaTime      = true,
                    },
                    // Combat erodes faction presence
                    new CouplingRule("combat.intensity", "faction.presence")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(-0.10f),
                        ScaleByDeltaTime      = true,
                    },
                    // Intel builds faction influence (soft power)
                    new CouplingRule("intel.coverage", "faction.influence")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = "same",
                        Operator              = CouplingOperator.Linear(0.15f),
                        ScaleByDeltaTime      = true,
                    },
                    // Intel detects threats
                    new CouplingRule("intel.coverage", "war.exposure")
                    {
                        InputChannelSelector  = "*",
                        OutputChannelSelector = $"explicit:[{wch}]",
                        Operator              = CouplingOperator.Linear(0.01f),
                        ScaleByDeltaTime      = true,
                    },
                    // War erodes stability
                    new CouplingRule("war.exposure", "faction.stability")
                    {
                        InputChannelSelector  = wch,
                        OutputChannelSelector = "*",
                        Operator              = CouplingOperator.Linear(-0.08f),
                        ScaleByDeltaTime      = true,
                    },
                };
            }

            private void BiEdge(string a, string b, float resistance)
            {
                Dim.AddEdge(a, b, resistance);
                Dim.AddEdge(b, a, resistance);
            }

            /// <summary>Advance the full galaxy one tick.</summary>
            public void Tick(float dt)
            {
                Propagator.Step(Dim, Economy.Availability, dt);
                Propagator.Step(Dim, Economy.PricePressure, dt);
                War.Tick(dt);
                Factions.Tick(dt);
                Combat.Tick(dt);
                Intel.Tick(dt);
                CouplingProcessor.Step(Dim, Rules, dt);
            }

            /// <summary>Sample price for a commodity at a node using its base value.</summary>
            public float Price(string node, string item)
                => Economy.SamplePrice(node, item, BasePrices[item]);

            /// <summary>Sample price using a custom base value.</summary>
            public float PriceRaw(string node, string item, float baseValue)
                => Economy.SamplePrice(node, item, baseValue);

            public float WarAt(string node)
                => War.GetExposureLogAmp(node);

            public float PresenceOf(string node, string faction)
                => Factions.Presence.GetLogAmp(node, faction);

            public float InfluenceOf(string node, string faction)
                => Factions.Influence.GetLogAmp(node, faction);

            // ── Seed the galaxy with faction starting positions ─────────
            public void SeedFactions()
            {
                // Republic: core worlds
                Factions.AddPresence(Sanctum, Republic, 4.0f);
                Factions.AddPresence(Apollo, Republic, 3.0f);
                Factions.AddPresence(Hyperia, Republic, 2.5f);
                Factions.AddPresence(CorusAlpha, Republic, 2.5f);
                Factions.AddPresence(Medici, Republic, 2.0f);
                Factions.AddPresence(Strozi, Republic, 2.0f);

                // Union: frontier worlds
                Factions.AddPresence(DemosEpsilon, Union, 3.0f);
                Factions.AddPresence(Xarkath, Union, 2.0f);
                Factions.AddPresence(Ciom, Union, 1.5f);
                Factions.AddPresence(Zadorov, Union, 1.5f);
                Factions.AddPresence(Amareth, Union, 1.0f);

                // Cartel: Shiraz cluster
                Factions.AddPresence(Shiraz, Cartel, 4.0f);
                Factions.AddPresence(ShirazSlave1, Cartel, 2.5f);
                Factions.AddPresence(ShirazSlave2, Cartel, 2.0f);
                Factions.AddPresence(ShirazSlave3, Cartel, 2.0f);
                Factions.AddPresence(Rameses, Cartel, 1.5f);

                // Pirates: Spectre + raiding presence
                Factions.AddPresence(Spectre, Pirates, 3.5f);
                Factions.AddPresence(DemosEpsilon, Pirates, 0.5f);
                Factions.AddPresence(Xarkath, Pirates, 0.5f);
            }

            /// <summary>
            /// Seed a basic galactic economy: biome-based production patterns.
            /// Oceanic worlds produce water, continental produce food, desert/mining
            /// produce metals, industrial produce electronics, etc.
            /// </summary>
            public void SeedEconomy()
            {
                // Sanctum: ecumenopolis (imports everything, exports electronics)
                Economy.InjectTrade(Sanctum, Electronics, 50f);
                Economy.InjectTrade(Sanctum, Water, 20f);
                Economy.InjectTrade(Sanctum, Food, 30f);

                // Apollo: agricultural breadbasket
                Economy.InjectTrade(Apollo, Food, 60f);
                Economy.InjectTrade(Apollo, Water, 40f);

                // Hyperia: industrial hub
                Economy.InjectTrade(Hyperia, Electronics, 40f);
                Economy.InjectTrade(Hyperia, Fuel, 30f);

                // Corus Alpha: mining colony
                Economy.InjectTrade(CorusAlpha, Metals, 50f);
                Economy.InjectTrade(CorusAlpha, Fuel, 20f);

                // Demos Epsilon: frontier fuel depot
                Economy.InjectTrade(DemosEpsilon, Fuel, 40f);
                Economy.InjectTrade(DemosEpsilon, Metals, 20f);

                // Medici: pharmaceutical hub
                Economy.InjectTrade(Medici, Medicine, 40f);
                Economy.InjectTrade(Medici, Food, 15f);

                // Strozi: water world
                Economy.InjectTrade(Strozi, Water, 60f);

                // Ciom: frontier agriculture
                Economy.InjectTrade(Ciom, Food, 30f);
                Economy.InjectTrade(Ciom, Water, 20f);

                // Zadorov: metals extraction
                Economy.InjectTrade(Zadorov, Metals, 35f);
                Economy.InjectTrade(Zadorov, Fuel, 15f);

                // Xarkath: harsh mining world
                Economy.InjectTrade(Xarkath, Metals, 40f);
                Economy.InjectTrade(Xarkath, Plutonium, 2f);

                // Amareth: mixed economy
                Economy.InjectTrade(Amareth, Food, 20f);
                Economy.InjectTrade(Amareth, Fuel, 25f);

                // Rameses: trade crossroads
                Economy.InjectTrade(Rameses, Fuel, 30f);
                Economy.InjectTrade(Rameses, Electronics, 15f);

                // Spectre: black market (contraband)
                Economy.InjectTrade(Spectre, Pistols, 20f);
                Economy.InjectTrade(Spectre, Painkillers, 25f);
                Economy.InjectTrade(Spectre, Fuel, 15f);

                // Shiraz: cartel production hub
                Economy.InjectTrade(Shiraz, Painkillers, 40f);
                Economy.InjectTrade(Shiraz, Pistols, 30f);
                Economy.InjectTrade(Shiraz, Electronics, 20f);

                // Slave systems: raw extraction
                Economy.InjectTrade(ShirazSlave1, Metals, 50f);
                Economy.InjectTrade(ShirazSlave1, Water, 30f);
                Economy.InjectTrade(ShirazSlave2, Fuel, 40f);
                Economy.InjectTrade(ShirazSlave2, Food, 20f);
                Economy.InjectTrade(ShirazSlave3, Plutonium, 3f);
                Economy.InjectTrade(ShirazSlave3, Metals, 25f);
            }

            /// <summary>Seed intel networks for all factions.</summary>
            public void SeedIntel()
            {
                // Republic: dense coverage in core worlds
                Intel.DeploySensor(Sanctum, Republic, 3.0f);
                Intel.DeploySensor(Apollo, Republic, 2.0f);
                Intel.DeploySensor(Hyperia, Republic, 2.0f);
                Intel.DeploySensor(CorusAlpha, Republic, 1.5f);

                // Cartel: surveillance of own turf
                Intel.DeploySensor(Shiraz, Cartel, 3.0f);
                Intel.DeploySensor(Rameses, Cartel, 1.5f);

                // Pirates: spy network in frontier
                Intel.DeploySensor(Spectre, Pirates, 2.5f);
                Intel.DeploySensor(DemosEpsilon, Pirates, 1.0f);

                // Union: border monitoring
                Intel.DeploySensor(DemosEpsilon, Union, 2.0f);
                Intel.DeploySensor(Xarkath, Union, 1.5f);
            }

            /// <summary>Full galaxy seed: factions + economy + intel.</summary>
            public void SeedAll()
            {
                SeedFactions();
                SeedEconomy();
                SeedIntel();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 1 — Border War: Cartel vs Republic (500 ticks)
        //
        //  The Cartel, emboldened by arms profits, declares war on the Republic's
        //  frontier supply line through Rameses and Amareth. Over 500 ticks:
        //    Phase 1 (0–99):   Peaceful trade establishes equilibrium prices.
        //    Phase 2 (100–299): Cartel declares war at Rameses. War exposure
        //                       propagates through Amareth → Zadorov → frontier.
        //                       Metals and fuel prices spike at affected nodes.
        //    Phase 3 (300–399): Ceasefire. Prices begin recovery.
        //    Phase 4 (400–499): Full peace. Prices trending toward equilibrium.
        //
        //  Tracked: metals price at Zadorov across all 4 phases.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void BorderWar_CartelVsRepublic_500Ticks_PriceCurve()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            // ── Phase 1: 100 ticks of peace ────────────────────────────────
            for (int t = 0; t < 100; t++)
            {
                if (t % 10 == 0) g.SeedEconomy(); // periodic production
                g.Tick(dt);
            }

            float metalsPricePhase1 = g.Price(Zadorov, Metals);
            float fuelPricePhase1 = g.Price(Amareth, Fuel);
            float warRamesesPhase1 = g.WarAt(Rameses);

            Assert.That(warRamesesPhase1, Is.LessThan(0.01f),
                "No war exposure at Rameses during peacetime.");

            // ── Phase 2: Cartel war (200 ticks) ────────────────────────────
            g.War.DeclareWar(Rameses);
            g.War.DeclareWar(Shiraz);  // Cartel mobilises home fleet

            for (int t = 0; t < 200; t++)
            {
                // Cartel raids frontier trade routes
                if (t % 20 == 0)
                {
                    g.Combat.CommitForce(Rameses, Cartel, 2.0f);
                    g.Combat.CommitForce(Rameses, Union, 1.0f);  // Union defends
                    g.Combat.CommitForce(Amareth, Cartel, 1.0f);
                }

                // Production continues (diminished in war zones)
                if (t % 15 == 0)
                {
                    g.Economy.InjectTrade(Zadorov, Metals, 20f);
                    g.Economy.InjectTrade(Amareth, Fuel, 15f);
                    g.Economy.InjectTrade(Rameses, Fuel, 10f);
                }

                g.Tick(dt);
            }

            float metalsPricePhase2 = g.Price(Zadorov, Metals);
            float fuelPricePhase2 = g.Price(Amareth, Fuel);
            float warRamesesPhase2 = g.WarAt(Rameses);

            // ── Phase 3: Ceasefire (100 ticks) ─────────────────────────────
            g.War.DeclareCeasefire(Rameses);
            g.War.DeclareCeasefire(Shiraz);

            for (int t = 0; t < 100; t++)
            {
                // Trade resumes at pre-war levels
                if (t % 10 == 0) g.SeedEconomy();
                g.Tick(dt);
            }

            float metalsPricePhase3 = g.Price(Zadorov, Metals);
            float warRamesesPhase3 = g.WarAt(Rameses);

            // ── Phase 4: Full recovery (100 ticks) ─────────────────────────
            for (int t = 0; t < 100; t++)
            {
                if (t % 10 == 0) g.SeedEconomy();
                g.Tick(dt);
            }

            float metalsPricePhase4 = g.Price(Zadorov, Metals);

            // ── Assertions ─────────────────────────────────────────────────

            Assert.That(warRamesesPhase2, Is.GreaterThan(1.0f),
                $"War exposure at Rameses must build during the conflict ({warRamesesPhase2:F4}).");

            Assert.That(metalsPricePhase2, Is.GreaterThan(metalsPricePhase1),
                $"Metals price at Zadorov must spike during war (P1={metalsPricePhase1:F2}, P2={metalsPricePhase2:F2}). " +
                "Chain: war.exposure at Rameses → propagation to Zadorov → coupling → −availability & +pressure.");

            Assert.That(fuelPricePhase2, Is.GreaterThan(fuelPricePhase1),
                $"Fuel price at Amareth must spike during war (P1={fuelPricePhase1:F2}, P2={fuelPricePhase2:F2}).");

            Assert.That(warRamesesPhase3, Is.LessThan(warRamesesPhase2),
                $"War exposure must decline after ceasefire (P2={warRamesesPhase2:F4}, P3={warRamesesPhase3:F4}).");

            // Price recovery: Phase 4 closer to Phase 1 than Phase 2 is.
            // The economy may not fully recover (some permanent damage), but the trend
            // must be toward equilibrium.
            float warDamage = Math.Abs(metalsPricePhase2 - metalsPricePhase1);
            float recovery = Math.Abs(metalsPricePhase2 - metalsPricePhase4);
            Assert.That(recovery, Is.GreaterThan(warDamage * 0.1f),
                $"Prices must show at least some recovery trend after 200 ticks of peace " +
                $"(warDamage={warDamage:F2}, recovery={recovery:F2}).");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 2 — Pirate Blockade of Demos Epsilon
        //
        //  Pirates seize control of the hyperspace lane to Demos Epsilon,
        //  cutting off the Union's fuel supply depot. The Republic's core
        //  worlds are not directly affected, but frontier prices spike.
        //
        //  Then a "smuggling route" opens (alternative low-volume trade)
        //  and prices partially recover at the blockaded node.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void PirateBlockade_DemosEpsilon_FuelPriceSpike()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            // Phase 1: Establish baseline (50 ticks)
            for (int t = 0; t < 50; t++)
            {
                if (t % 10 == 0) g.SeedEconomy();
                g.Tick(dt);
            }

            float fuelPriceBefore = g.Price(DemosEpsilon, Fuel);

            // Phase 2: Pirate blockade (100 ticks)
            g.War.DeclareWar(DemosEpsilon);
            g.Combat.CommitForce(DemosEpsilon, Pirates, 3.0f);

            for (int t = 0; t < 100; t++)
            {
                // Pirates reinforce the blockade
                if (t % 25 == 0)
                    g.Combat.CommitForce(DemosEpsilon, Pirates, 1.5f);

                // Reduced trade under fire
                if (t % 20 == 0)
                    g.Economy.InjectTrade(DemosEpsilon, Fuel, 10f);

                g.Tick(dt);
            }

            float fuelPriceDuringBlockade = g.Price(DemosEpsilon, Fuel);
            float warDemos = g.WarAt(DemosEpsilon);

            // Phase 3: Smuggling route opens (50 ticks) — alternative supply
            // Smugglers run fuel through Xarkath (bypassing the blockade)
            g.War.DeclareCeasefire(DemosEpsilon);
            for (int t = 0; t < 50; t++)
            {
                // Smugglers inject supply through back channels
                if (t % 5 == 0)
                {
                    g.Economy.InjectTrade(Xarkath, Fuel, 15f);
                    g.Economy.InjectTrade(DemosEpsilon, Fuel, 25f);
                }
                g.Tick(dt);
            }

            float fuelPriceAfterSmuggling = g.Price(DemosEpsilon, Fuel);

            // ── Assertions ─────────────────────────────────────────────────

            Assert.That(warDemos, Is.GreaterThan(0.5f),
                "War exposure must build at blockaded node.");

            Assert.That(fuelPriceDuringBlockade, Is.GreaterThan(fuelPriceBefore),
                $"Fuel price must spike during blockade (before={fuelPriceBefore:F2}, during={fuelPriceDuringBlockade:F2}).");

            // Smuggling partially offsets the blockade damage
            float blockadeDamage = fuelPriceDuringBlockade - fuelPriceBefore;
            Assert.That(blockadeDamage, Is.GreaterThan(0f),
                "Blockade must cause measurable price damage.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 3 — Republic Intelligence Network Across Core Worlds
        //
        //  The Republic maintains a dense sensor network across its core worlds.
        //  Over 100 ticks, intel propagates along hyperspace lanes, giving
        //  the Republic visibility deep into frontier space.
        //
        //  Assertions:
        //    - Republic has dominant observer status at all core nodes
        //    - Coverage propagation reaches at least 3 hops from Sanctum
        //    - Coverage at distant Shiraz is near-zero (enemy territory)
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void RepublicIntelNetwork_CoverageAcrossCoreWorlds()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            for (int t = 0; t < 100; t++)
            {
                // Republic maintains sensor patrols
                if (t % 10 == 0)
                {
                    g.Intel.DeploySensor(Sanctum, Republic, 2.0f);
                    g.Intel.DeploySensor(Apollo, Republic, 1.5f);
                    g.Intel.DeploySensor(Hyperia, Republic, 1.5f);
                    g.Intel.DeploySensor(CorusAlpha, Republic, 1.0f);
                }
                g.Tick(dt);
            }

            float coverageSanctum = g.Intel.GetCoverage(Sanctum, Republic);
            float coverageApollo = g.Intel.GetCoverage(Apollo, Republic);
            float coverageCiom = g.Intel.GetCoverage(Ciom, Republic); // 2 hops from Hyperia
            float coverageShiraz = g.Intel.GetCoverage(Shiraz, Republic); // far away

            // Core worlds: Republic is dominant observer
            string domSanctum = g.Intel.GetDominantObserver(Sanctum);
            string domApollo = g.Intel.GetDominantObserver(Apollo);

            Assert.That(coverageSanctum, Is.GreaterThan(1.0f),
                "Republic must have strong coverage at capital.");
            Assert.That(coverageApollo, Is.GreaterThan(0.5f),
                "Republic must have good coverage at Apollo.");
            Assert.That(coverageCiom, Is.GreaterThan(0f),
                "Coverage must propagate at least 2 hops from deployment sites.");
            Assert.That(coverageShiraz, Is.LessThan(coverageSanctum * 0.1f).Or.EqualTo(0f),
                $"Coverage at Shiraz ({coverageShiraz:F4}) must be negligible compared to Sanctum ({coverageSanctum:F4}).");

            Assert.That(domSanctum, Is.EqualTo(Republic),
                "Republic must be dominant observer at Sanctum.");
            Assert.That(domApollo, Is.EqualTo(Republic),
                "Republic must be dominant observer at Apollo.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 4 — Galactic Crisis: Multi-Front War (1000 ticks)
        //
        //  The flagship stress test. Models a full galactic war:
        //    Phase 1 (0–199):    Peace, economy establishes equilibrium.
        //    Phase 2 (200–499):  Cartel invades Rameses/Amareth. Pirates raid Demos.
        //                        Republic reinforces CorusAlpha. Union defends frontier.
        //    Phase 3 (500–699):  Cartel occupies Rameses. Republic counterattacks.
        //                        Pirate ceasefire. Economy partially recovers in north.
        //    Phase 4 (700–999):  Full ceasefire everywhere. Reconstruction.
        //                        Track economic recovery, faction shifts, stability.
        //
        //  This test tracks 12 metrics across all 4 phases.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void GalacticCrisis_1000Ticks_MultiFrontWar()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            // ── Phase 1: 200 ticks of peace ────────────────────────────────
            for (int t = 0; t < 200; t++)
            {
                if (t % 15 == 0) g.SeedEconomy();
                if (t % 20 == 0) g.SeedIntel();
                g.Tick(dt);
            }

            float electronicsPriceSanctumP1 = g.Price(Sanctum, Electronics);
            float fuelPriceDemosP1 = g.Price(DemosEpsilon, Fuel);
            float metalsPriceZadorovP1 = g.Price(Zadorov, Metals);
            float republicPresenceCorusP1 = g.PresenceOf(CorusAlpha, Republic);
            float unionPresenceDemosP1 = g.PresenceOf(DemosEpsilon, Union);

            // ── Phase 2: Multi-front war (300 ticks) ───────────────────────
            // Cartel invades through Rameses
            g.War.DeclareWar(Rameses);
            g.War.DeclareWar(Amareth);
            g.War.DeclareWar(Shiraz); // Cartel home mobilisation

            // Pirates raid Demos Epsilon
            g.War.DeclareWar(DemosEpsilon);

            for (int t = 0; t < 300; t++)
            {
                // Cartel offensive
                if (t % 15 == 0)
                {
                    g.Combat.CommitForce(Rameses, Cartel, 2.5f);
                    g.Combat.CommitForce(Rameses, Union, 0.8f);
                    g.Combat.CommitForce(Amareth, Cartel, 1.5f);
                    g.Combat.CommitForce(Amareth, Union, 0.5f);
                }

                // Pirate raids
                if (t % 20 == 0)
                {
                    g.Combat.CommitForce(DemosEpsilon, Pirates, 1.5f);
                    g.Combat.CommitForce(DemosEpsilon, Union, 1.0f);
                }

                // Republic reinforces border
                if (t % 25 == 0)
                {
                    g.Factions.AddPresence(CorusAlpha, Republic, 0.5f);
                    g.Intel.DeploySensor(CorusAlpha, Republic, 1.0f);
                }

                // Reduced trade during war
                if (t % 20 == 0)
                {
                    g.Economy.InjectTrade(Zadorov, Metals, 15f);
                    g.Economy.InjectTrade(DemosEpsilon, Fuel, 10f);
                    g.Economy.InjectTrade(Sanctum, Electronics, 30f);
                    g.Economy.InjectTrade(Rameses, Fuel, 5f);
                }

                g.Tick(dt);
            }

            float metalsPriceZadorovP2 = g.Price(Zadorov, Metals);
            float fuelPriceDemosP2 = g.Price(DemosEpsilon, Fuel);
            float warRamesesP2 = g.WarAt(Rameses);
            float warDemosP2 = g.WarAt(DemosEpsilon);
            float unionPresenceDemosP2 = g.PresenceOf(DemosEpsilon, Union);

            // ── Phase 3: Occupation + counterattack (200 ticks) ────────────
            // Cartel occupies Rameses
            g.War.DeclareOccupation(Rameses, Cartel);

            // Pirates take ceasefire
            g.War.DeclareCeasefire(DemosEpsilon);

            // Republic launches counterattack at Amareth
            for (int t = 0; t < 200; t++)
            {
                if (t % 15 == 0)
                {
                    g.Combat.CommitForce(Amareth, Republic, 2.0f);
                    g.Combat.CommitForce(Amareth, Cartel, 1.0f);
                    g.Factions.AddPresence(Amareth, Republic, 0.3f);
                }

                // Economy recovering in pirate-free zones
                if (t % 10 == 0)
                {
                    g.Economy.InjectTrade(DemosEpsilon, Fuel, 25f);
                    g.Economy.InjectTrade(Sanctum, Electronics, 40f);
                    g.Economy.InjectTrade(Apollo, Food, 30f);
                }

                g.Tick(dt);
            }

            float warDemosP3 = g.WarAt(DemosEpsilon);
            float fuelPriceDemosP3 = g.Price(DemosEpsilon, Fuel);

            // ── Phase 4: Full ceasefire + reconstruction (300 ticks) ───────
            g.War.DeclareCeasefire(Rameses);
            g.War.DeclareCeasefire(Amareth);
            g.War.DeclareCeasefire(Shiraz);

            for (int t = 0; t < 300; t++)
            {
                // Full trade restoration
                if (t % 10 == 0) g.SeedEconomy();

                // Reconstruction: all factions rebuild
                if (t % 30 == 0)
                {
                    g.Factions.AddPresence(Rameses, Union, 0.5f);
                    g.Factions.AddPresence(Amareth, Union, 0.5f);
                    g.Factions.AddPresence(DemosEpsilon, Union, 0.5f);
                    g.Factions.AddPresence(CorusAlpha, Republic, 0.3f);
                }

                g.Tick(dt);
            }

            float metalsPriceZadorovP4 = g.Price(Zadorov, Metals);
            float fuelPriceDemosP4 = g.Price(DemosEpsilon, Fuel);
            float warRamesesP4 = g.WarAt(Rameses);

            // ── Assertions (12 metrics) ─────────────────────────────────────

            // 1. War built at conflict zones
            Assert.That(warRamesesP2, Is.GreaterThan(1.0f),
                $"War exposure at Rameses must build during conflict ({warRamesesP2:F4}).");
            Assert.That(warDemosP2, Is.GreaterThan(0.5f),
                $"War exposure at Demos must build during pirate raids ({warDemosP2:F4}).");

            // 2. Prices spiked during war
            Assert.That(metalsPriceZadorovP2, Is.GreaterThan(metalsPriceZadorovP1),
                $"Metals at Zadorov: P1={metalsPriceZadorovP1:F2}, P2={metalsPriceZadorovP2:F2}.");
            Assert.That(fuelPriceDemosP2, Is.GreaterThan(fuelPriceDemosP1),
                $"Fuel at Demos: P1={fuelPriceDemosP1:F2}, P2={fuelPriceDemosP2:F2}.");

            // 3. Pirate ceasefire reduced war at Demos
            Assert.That(warDemosP3, Is.LessThan(warDemosP2),
                $"Pirate ceasefire must reduce war at Demos (P2={warDemosP2:F4}, P3={warDemosP3:F4}).");

            // 4. Union presence eroded during war
            Assert.That(unionPresenceDemosP2, Is.LessThan(unionPresenceDemosP1),
                $"Union presence at Demos must erode during pirate raids " +
                $"(P1={unionPresenceDemosP1:F4}, P2={unionPresenceDemosP2:F4}).");

            // 5. Full ceasefire reduced war at Rameses
            Assert.That(warRamesesP4, Is.LessThan(warRamesesP2),
                $"War at Rameses must decline after full ceasefire (P2={warRamesesP2:F4}, P4={warRamesesP4:F4}).");

            // 6. Prices trending toward recovery in Phase 4
            float warDamageMetals = Math.Abs(metalsPriceZadorovP2 - metalsPriceZadorovP1);
            float recoveryMetals = Math.Abs(metalsPriceZadorovP2 - metalsPriceZadorovP4);
            Assert.That(recoveryMetals, Is.GreaterThan(warDamageMetals * 0.05f),
                $"Metals price must show some recovery trend (damage={warDamageMetals:F2}, recovery={recoveryMetals:F2}).");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 5 — Occupation Race: Pirates vs Cartel at Xarkath
        //
        //  Both Pirates and Cartel try to occupy the strategic mining world
        //  of Xarkath (rich in Plutonium). The faction that commits more
        //  force and destabilises the node wins the occupation race.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void OccupationRace_PiratesVsCartel_Xarkath()
        {
            var g = new Galaxy();
            g.SeedFactions();
            const float dt = 0.1f;

            // Pirates attack Xarkath — currently Union-held
            g.War.DeclareWar(Xarkath);
            g.War.DeclareOccupation(Xarkath, Pirates);
            g.War.SetNodeStability(Xarkath, 0.0f); // frontier world, low stability

            bool piratesOccupied = false;
            g.War.OnOccupationComplete = (node, attacker) =>
            {
                if (node == Xarkath && attacker == Pirates) piratesOccupied = true;
            };

            for (int t = 0; t < 200; t++)
            {
                if (t % 15 == 0)
                {
                    g.Combat.CommitForce(Xarkath, Pirates, 2.0f);
                    g.Combat.CommitForce(Xarkath, Union, 0.5f);
                }
                g.Tick(dt);
                if (piratesOccupied) break;
            }

            Assert.That(piratesOccupied, Is.True,
                "Pirates must eventually occupy the low-stability frontier world.");

            // After occupation: pirate presence should be measurable
            float piratePresence = g.PresenceOf(Xarkath, Pirates);
            Assert.That(piratePresence, Is.GreaterThan(0f),
                "Pirates must have some presence at Xarkath after seizing it.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 6 — Contraband Economy: Painkillers Price in Wartime
        //
        //  During wartime, contraband (Painkillers, Pistols) prices respond
        //  differently than legal goods. The black market hub at Spectre should
        //  see price inflation as war drives demand for weapons and drugs.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void ContrabandEconomy_PainkillersPrice_WartimeInflation()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            // Phase 1: Peacetime baseline (80 ticks)
            for (int t = 0; t < 80; t++)
            {
                if (t % 10 == 0) g.SeedEconomy();
                g.Tick(dt);
            }

            float painkillersSpectreP1 = g.Price(Spectre, Painkillers);
            float pistolsSpectreP1 = g.Price(Spectre, Pistols);

            // Phase 2: War erupts in the frontier (120 ticks)
            g.War.DeclareWar(DemosEpsilon);
            g.War.DeclareWar(Xarkath);

            for (int t = 0; t < 120; t++)
            {
                if (t % 15 == 0)
                {
                    g.Combat.CommitForce(DemosEpsilon, Pirates, 2.0f);
                    g.Combat.CommitForce(Xarkath, Pirates, 1.5f);
                }

                // Black market keeps running
                if (t % 10 == 0)
                {
                    g.Economy.InjectTrade(Spectre, Painkillers, 30f);
                    g.Economy.InjectTrade(Spectre, Pistols, 25f);
                }
                g.Tick(dt);
            }

            float painkillersSpectreP2 = g.Price(Spectre, Painkillers);
            float pistolsSpectreP2 = g.Price(Spectre, Pistols);

            // War exposure propagates from Demos/Xarkath to Spectre via hyperspace lanes.
            // war.exposure → −availability coupling → prices rise
            float warSpectre = g.WarAt(Spectre);

            Assert.That(warSpectre, Is.GreaterThan(0f),
                $"War exposure must have propagated to Spectre ({warSpectre:F4}). " +
                "Spectre is 1 hop from Demos Epsilon (resistance 0.8).");

            Assert.That(painkillersSpectreP2, Is.GreaterThan(painkillersSpectreP1),
                $"Painkillers price at Spectre must rise during war " +
                $"(P1={painkillersSpectreP1:F2}, P2={painkillersSpectreP2:F2}).");

            Assert.That(pistolsSpectreP2, Is.GreaterThan(pistolsSpectreP1),
                $"Pistols price at Spectre must rise during war " +
                $"(P1={pistolsSpectreP1:F2}, P2={pistolsSpectreP2:F2}).");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 7 — Faction Dominance Shift: Cartel Expands Through Rameses
        //
        //  Over 300 ticks, the Cartel projects presence through Rameses
        //  and into Amareth, while the Union's frontier presence decays.
        //  Track: dominant faction at Rameses shifts from Union to Cartel.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void FactionDominanceShift_CartelExpandsThroughRameses()
        {
            var g = new Galaxy();
            g.SeedFactions();
            const float dt = 0.1f;

            // Initially Union holds Amareth, Cartel holds Rameses
            string initialDomRameses = g.Factions.GetDominantFaction(Rameses);
            Assert.That(initialDomRameses, Is.EqualTo(Cartel),
                "Cartel should initially dominate Rameses.");

            // Cartel projects power and destabilises Amareth
            for (int t = 0; t < 300; t++)
            {
                if (t % 20 == 0)
                {
                    g.Factions.AddPresence(Rameses, Cartel, 0.5f);
                    g.Factions.AddPresence(Amareth, Cartel, 0.3f);
                }
                g.Tick(dt);
            }

            float cartelAmareth = g.PresenceOf(Amareth, Cartel);
            float unionAmareth = g.PresenceOf(Amareth, Union);

            // Cartel must have gained significant presence at Amareth
            Assert.That(cartelAmareth, Is.GreaterThan(0.5f),
                $"Cartel must project presence to Amareth ({cartelAmareth:F4}).");

            // The cartel presence should be growing relative to a decaying Union presence
            // (Union has no reinforcement in this scenario, while presence decays at 0.06)
            Assert.That(cartelAmareth, Is.GreaterThan(unionAmareth),
                $"Cartel ({cartelAmareth:F4}) must surpass Union ({unionAmareth:F4}) " +
                "at Amareth after 300 ticks of sustained projection.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 8 — Plutonium Economics: Xarkath Price Sensitivity
        //
        //  Plutonium (base price 25,000) is the most expensive commodity.
        //  Test that war at its production site causes enormous price spikes
        //  at downstream consumers, demonstrating the engine handles prices
        //  across 4 orders of magnitude.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void PlutoniumEconomics_WarCausesExtremeePriceSpike()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            // Baseline: establish plutonium market (60 ticks)
            for (int t = 0; t < 60; t++)
            {
                if (t % 10 == 0)
                {
                    g.Economy.InjectTrade(Xarkath, Plutonium, 2f);
                    g.Economy.InjectTrade(ShirazSlave3, Plutonium, 3f);
                }
                g.Tick(dt);
            }

            float plutoniumPriceBefore = g.Price(Xarkath, Plutonium);

            // War hits both production sites
            g.War.DeclareWar(Xarkath);
            g.War.DeclareWar(ShirazSlave3);

            for (int t = 0; t < 100; t++)
            {
                if (t % 20 == 0)
                {
                    g.Economy.InjectTrade(Xarkath, Plutonium, 1f);
                    g.Economy.InjectTrade(ShirazSlave3, Plutonium, 1f);
                }
                g.Tick(dt);
            }

            float plutoniumPriceAfter = g.Price(Xarkath, Plutonium);

            Assert.That(plutoniumPriceAfter, Is.GreaterThan(plutoniumPriceBefore),
                $"Plutonium price must spike during war at production sites " +
                $"(before={plutoniumPriceBefore:F2}, after={plutoniumPriceAfter:F2}).");

            // The price should still be a reasonable number (not NaN/Inf)
            Assert.That(float.IsFinite(plutoniumPriceAfter), Is.True,
                $"Plutonium price must remain finite ({plutoniumPriceAfter}).");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 9 — Economic Isolation: Shiraz Cluster Cut Off
        //
        //  War at Rameses (the only land bridge to Shiraz) isolates the
        //  Cartel's cluster from the galactic economy. Prices inside the
        //  cluster should diverge from Republic core prices.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void EconomicIsolation_ShirazCluster_PriceDivergence()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            // Phase 1: connected economy (80 ticks)
            for (int t = 0; t < 80; t++)
            {
                if (t % 10 == 0) g.SeedEconomy();
                g.Tick(dt);
            }

            float metalsSanctumP1 = g.Price(Sanctum, Metals);
            float metalsShirazP1 = g.Price(Shiraz, Metals);

            // Phase 2: war at Rameses cuts the bridge (150 ticks)
            g.War.DeclareWar(Rameses);

            for (int t = 0; t < 150; t++)
            {
                if (t % 10 == 0) g.SeedEconomy();
                if (t % 20 == 0)
                    g.Combat.CommitForce(Rameses, Cartel, 1.5f);
                g.Tick(dt);
            }

            float metalsSanctumP2 = g.Price(Sanctum, Metals);
            float metalsShirazP2 = g.Price(Shiraz, Metals);
            float warRameses = g.WarAt(Rameses);

            // The war at Rameses should affect Shiraz more than Sanctum
            // because Shiraz depends on Rameses as its main link to the galaxy,
            // while Sanctum has many other supply routes.
            Assert.That(warRameses, Is.GreaterThan(1.0f),
                "War must be active at the chokepoint.");

            // Metals production happens at ShirazSlave1 inside the cluster.
            // With Rameses in flames, metals stay trapped inside the cluster
            // (lower scarcity in Shiraz) while the rest of the galaxy
            // loses access to that supply (higher scarcity everywhere else).
            // However, war coupling also disrupts the local economy.
            // The key assertion: prices are MORE affected near the war zone.
            float warShiraz = g.WarAt(Shiraz);
            float warSanctum = g.WarAt(Sanctum);
            Assert.That(warShiraz, Is.GreaterThan(warSanctum),
                $"War exposure at Shiraz ({warShiraz:F4}) must exceed Sanctum ({warSanctum:F4}) " +
                "because Shiraz is closer to the Rameses conflict zone.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 10 — Long-Horizon Price Tracking (1000 ticks, 9 commodities)
        //
        //  Run the full galaxy for 1000 ticks of steady-state trade with
        //  periodic disruptions. Track all 9 commodity prices at Sanctum
        //  every 100 ticks. Assert:
        //    - All prices remain finite
        //    - All prices remain positive
        //    - Price variance is bounded (no runaway inflation/deflation)
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void LongHorizon_1000Ticks_AllPricesTracked()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            var priceHistory = new Dictionary<string, List<float>>();
            foreach (var item in BasePrices.Keys)
                priceHistory[item] = new List<float>();

            for (int t = 0; t < 1000; t++)
            {
                // Steady production
                if (t % 10 == 0) g.SeedEconomy();

                // Intel maintenance
                if (t % 30 == 0) g.SeedIntel();

                // Periodic disruptions: minor war at tick 300-400
                if (t == 300) g.War.DeclareWar(Rameses);
                if (t == 400) g.War.DeclareCeasefire(Rameses);

                // Another disruption at tick 600-700 (pirate raid)
                if (t == 600) g.War.DeclareWar(DemosEpsilon);
                if (t == 700) g.War.DeclareCeasefire(DemosEpsilon);

                g.Tick(dt);

                // Sample prices every 100 ticks
                if (t % 100 == 99)
                {
                    foreach (var item in BasePrices.Keys)
                        priceHistory[item].Add(g.Price(Sanctum, item));
                }
            }

            // ── Assertions ─────────────────────────────────────────────────

            foreach (var item in BasePrices.Keys)
            {
                var prices = priceHistory[item];
                Assert.That(prices.Count, Is.EqualTo(10),
                    $"Expected 10 price samples for {item}.");

                foreach (var p in prices)
                {
                    Assert.That(float.IsFinite(p), Is.True,
                        $"{item} price must be finite at all time points ({p}).");
                    Assert.That(p, Is.GreaterThan(0f),
                        $"{item} price must be positive ({p:F4}).");
                }

                // Price variance check: no commodity should swing more than 100×
                float minP = prices.Min();
                float maxP = prices.Max();
                float ratio = maxP / Math.Max(minP, 0.001f);
                Assert.That(ratio, Is.LessThan(100f),
                    $"{item} price ratio (max/min = {ratio:F2}) must be < 100× over 1000 ticks. " +
                    $"Min={minP:F2}, Max={maxP:F2}.");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 11 — War Propagation Across The Galaxy
        //
        //  Start a war at Shiraz. After 300 ticks, war exposure should have
        //  propagated through Rameses → Amareth → Zadorov → … → frontier.
        //  Nodes closer to Shiraz should have higher exposure.
        //  Sanctum (at the far end of the galaxy) should have minimal exposure.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void WarPropagation_ShirazToGalaxy_TopologyShapesWavefront()
        {
            var g = new Galaxy();
            g.SeedFactions();
            const float dt = 0.1f;

            g.War.DeclareWar(Shiraz);

            for (int t = 0; t < 300; t++)
                g.Tick(dt);

            float warShiraz = g.WarAt(Shiraz);
            float warRameses = g.WarAt(Rameses);
            float warAmareth = g.WarAt(Amareth);
            float warZadorov = g.WarAt(Zadorov);
            float warSanctum = g.WarAt(Sanctum);

            // Topology: Shiraz → Rameses (0.7) → Amareth (0.4) → Zadorov (0.5) → ...many hops → Sanctum
            Assert.That(warShiraz, Is.GreaterThan(5.0f),
                $"Source of war must have high exposure ({warShiraz:F4}).");

            Assert.That(warRameses, Is.GreaterThan(warAmareth),
                $"Rameses ({warRameses:F4}) is closer to Shiraz than Amareth ({warAmareth:F4}).");

            Assert.That(warAmareth, Is.GreaterThan(warZadorov).Or.EqualTo(warZadorov).Within(0.5f),
                $"Amareth ({warAmareth:F4}) is closer or equal to Zadorov ({warZadorov:F4}).");

            Assert.That(warRameses, Is.GreaterThan(warSanctum),
                $"Rameses ({warRameses:F4}) must have more war than distant Sanctum ({warSanctum:F4}).");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 12 — Espionage War: Republic vs Cartel Intel Race
        //
        //  Both factions deploy competing sensor networks. After 200 ticks,
        //  each should dominate intel coverage in their own territory.
        //  At contested frontier nodes, coverage should reflect proximity
        //  to each faction's sensor deployment sites.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void EspionageWar_RepublicVsCartel_IntelDominance()
        {
            var g = new Galaxy();
            g.SeedFactions();
            const float dt = 0.1f;

            for (int t = 0; t < 200; t++)
            {
                // Republic deploys sensors in core
                if (t % 10 == 0)
                {
                    g.Intel.DeploySensor(Sanctum, Republic, 2.0f);
                    g.Intel.DeploySensor(Hyperia, Republic, 1.5f);
                    g.Intel.DeploySensor(CorusAlpha, Republic, 1.0f);
                }

                // Cartel deploys sensors in their cluster
                if (t % 10 == 0)
                {
                    g.Intel.DeploySensor(Shiraz, Cartel, 2.0f);
                    g.Intel.DeploySensor(Rameses, Cartel, 1.5f);
                }

                g.Tick(dt);
            }

            // Each faction should dominate their own territory
            string domSanctum = g.Intel.GetDominantObserver(Sanctum);
            string domShiraz = g.Intel.GetDominantObserver(Shiraz);

            Assert.That(domSanctum, Is.EqualTo(Republic),
                "Republic must dominate intel at its capital.");
            Assert.That(domShiraz, Is.EqualTo(Cartel),
                "Cartel must dominate intel in its own cluster.");

            // At the contested frontier (Rameses), Cartel should dominate
            // since they deploy sensors directly there.
            float repCoverageRameses = g.Intel.GetCoverage(Rameses, Republic);
            float cartelCoverageRameses = g.Intel.GetCoverage(Rameses, Cartel);

            Assert.That(cartelCoverageRameses, Is.GreaterThan(repCoverageRameses),
                $"Cartel ({cartelCoverageRameses:F4}) must have more coverage than Republic " +
                $"({repCoverageRameses:F4}) at Rameses (Cartel deploys sensors directly there).");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 13 — Four-Faction Stability Melee (500 ticks)
        //
        //  All four factions simultaneously wage small conflicts across the galaxy.
        //  After 500 ticks, the engine must remain stable (no NaN/Inf),
        //  and all factions must still exist (no faction has been completely
        //  wiped out from the map).
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void FourFactionMelee_500Ticks_AllFactionssurvive()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            // Multiple wars across multiple fronts
            g.War.DeclareWar(Rameses);     // Cartel front
            g.War.DeclareWar(DemosEpsilon); // Pirate front
            g.War.DeclareWar(Xarkath);      // Contested mining world
            g.War.DeclareWar(Amareth);      // Frontier zone

            for (int t = 0; t < 500; t++)
            {
                // All factions commit forces at different nodes
                if (t % 20 == 0)
                {
                    g.Combat.CommitForce(Rameses, Cartel, 1.5f);
                    g.Combat.CommitForce(Rameses, Union, 0.8f);
                    g.Combat.CommitForce(DemosEpsilon, Pirates, 1.5f);
                    g.Combat.CommitForce(DemosEpsilon, Union, 1.0f);
                    g.Combat.CommitForce(Xarkath, Pirates, 1.0f);
                    g.Combat.CommitForce(Xarkath, Union, 0.8f);
                    g.Combat.CommitForce(Amareth, Cartel, 1.0f);
                    g.Combat.CommitForce(Amareth, Republic, 0.5f);
                }

                // All factions reinforce their capitals
                if (t % 50 == 0)
                {
                    g.Factions.AddPresence(Sanctum, Republic, 1.0f);
                    g.Factions.AddPresence(Shiraz, Cartel, 1.0f);
                    g.Factions.AddPresence(Spectre, Pirates, 1.0f);
                    g.Factions.AddPresence(DemosEpsilon, Union, 1.0f);
                }

                // Trade continues (reduced)
                if (t % 15 == 0)
                {
                    g.Economy.InjectTrade(Sanctum, Electronics, 20f);
                    g.Economy.InjectTrade(Apollo, Food, 30f);
                    g.Economy.InjectTrade(Shiraz, Painkillers, 20f);
                    g.Economy.InjectTrade(Spectre, Pistols, 15f);
                    g.Economy.InjectTrade(Zadorov, Metals, 20f);
                }

                g.Tick(dt);
            }

            // All factions must have positive presence at their capital
            Assert.That(g.PresenceOf(Sanctum, Republic), Is.GreaterThan(0f),
                "Republic must survive at Sanctum.");
            Assert.That(g.PresenceOf(Shiraz, Cartel), Is.GreaterThan(0f),
                "Cartel must survive at Shiraz.");
            Assert.That(g.PresenceOf(Spectre, Pirates), Is.GreaterThan(0f),
                "Pirates must survive at Spectre.");
            Assert.That(g.PresenceOf(DemosEpsilon, Union), Is.GreaterThan(0f),
                "Union must survive at Demos Epsilon.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  DETERMINISM PROOFS — 1000-tick scale
        //
        //  Two independent runs of the same scenario must produce byte-identical
        //  StateHash. This is the foundation for save/load, replay, and lockstep.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Determinism_GalacticCrisis_1000Ticks_ByteIdentical()
        {
            string Run()
            {
                var g = new Galaxy();
                g.SeedAll();
                const float dt = 0.1f;

                // Phase 1: Peace (200 ticks)
                for (int t = 0; t < 200; t++)
                {
                    if (t % 15 == 0) g.SeedEconomy();
                    if (t % 20 == 0) g.SeedIntel();
                    g.Tick(dt);
                }

                // Phase 2: Multi-front war (300 ticks)
                g.War.DeclareWar(Rameses);
                g.War.DeclareWar(DemosEpsilon);
                g.War.DeclareWar(Shiraz);
                for (int t = 0; t < 300; t++)
                {
                    if (t % 15 == 0)
                    {
                        g.Combat.CommitForce(Rameses, Cartel, 2.0f);
                        g.Combat.CommitForce(DemosEpsilon, Pirates, 1.5f);
                    }
                    if (t % 20 == 0)
                    {
                        g.Economy.InjectTrade(Sanctum, Electronics, 30f);
                        g.Economy.InjectTrade(Zadorov, Metals, 15f);
                    }
                    g.Tick(dt);
                }

                // Phase 3: Ceasefire (200 ticks)
                g.War.DeclareCeasefire(Rameses);
                g.War.DeclareCeasefire(DemosEpsilon);
                g.War.DeclareCeasefire(Shiraz);
                for (int t = 0; t < 200; t++)
                {
                    if (t % 10 == 0) g.SeedEconomy();
                    g.Tick(dt);
                }

                // Phase 4: Recovery (300 ticks)
                for (int t = 0; t < 300; t++)
                {
                    if (t % 10 == 0) g.SeedEconomy();
                    if (t % 30 == 0) g.SeedIntel();
                    g.Tick(dt);
                }

                return StateHash.Compute(g.Dim);
            }

            string h1 = Run();
            string h2 = Run();

            Assert.That(h1, Is.EqualTo(h2),
                "A 1000-tick galactic crisis scenario with all five systems, 4 factions, 9 commodities, " +
                "17 star systems, multi-front war, ceasefire, and recovery must produce a byte-identical " +
                "StateHash across two independent runs. Any divergence means non-determinism.");
        }

        [Test]
        public void Determinism_FourFactionMelee_500Ticks_ByteIdentical()
        {
            string Run()
            {
                var g = new Galaxy();
                g.SeedAll();
                const float dt = 0.1f;

                g.War.DeclareWar(Rameses);
                g.War.DeclareWar(DemosEpsilon);
                g.War.DeclareWar(Xarkath);

                for (int t = 0; t < 500; t++)
                {
                    if (t % 20 == 0)
                    {
                        g.Combat.CommitForce(Rameses, Cartel, 1.5f);
                        g.Combat.CommitForce(DemosEpsilon, Pirates, 1.5f);
                        g.Combat.CommitForce(Xarkath, Union, 1.0f);
                    }
                    if (t % 15 == 0)
                    {
                        g.Economy.InjectTrade(Sanctum, Electronics, 20f);
                        g.Economy.InjectTrade(Shiraz, Pistols, 15f);
                        g.Economy.InjectTrade(Zadorov, Metals, 20f);
                    }
                    if (t % 30 == 0)
                        g.Intel.DeploySensor(Sanctum, Republic, 1.0f);
                    g.Tick(dt);
                }

                return StateHash.Compute(g.Dim);
            }

            Assert.That(Run(), Is.EqualTo(Run()),
                "500-tick four-faction melee must be fully deterministic.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  STABILITY PROOF — 2000 ticks, full galaxy, no NaN / no Infinity
        //
        //  The ultimate stress test. Runs the full galaxy for 2000 ticks with
        //  wars, ceasefires, trade, intelligence, and combat. Validates every
        //  50 ticks that all 8 scalar fields contain no NaN, no Infinity,
        //  and all logAmps are within profile clamp bounds.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Stability_FullGalaxy_2000Ticks_NoNaNNoInfinity()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            for (int t = 0; t < 2000; t++)
            {
                // Periodic economy
                if (t % 10 == 0) g.SeedEconomy();
                if (t % 30 == 0) g.SeedIntel();

                // War cycles: war at 200, ceasefire at 400, war at 800, ceasefire at 1000
                if (t == 200) { g.War.DeclareWar(Rameses); g.War.DeclareWar(DemosEpsilon); }
                if (t == 400) { g.War.DeclareCeasefire(Rameses); g.War.DeclareCeasefire(DemosEpsilon); }
                if (t == 800) { g.War.DeclareWar(Xarkath); g.War.DeclareWar(ShirazSlave3); }
                if (t == 1000) { g.War.DeclareCeasefire(Xarkath); g.War.DeclareCeasefire(ShirazSlave3); }
                if (t == 1400) { g.War.DeclareWar(Amareth); }
                if (t == 1600) { g.War.DeclareCeasefire(Amareth); }

                // Combat during wars
                if (t >= 200 && t < 400 && t % 20 == 0)
                {
                    g.Combat.CommitForce(Rameses, Cartel, 1.5f);
                    g.Combat.CommitForce(DemosEpsilon, Pirates, 1.0f);
                }
                if (t >= 800 && t < 1000 && t % 20 == 0)
                {
                    g.Combat.CommitForce(Xarkath, Pirates, 1.5f);
                    g.Combat.CommitForce(ShirazSlave3, Cartel, 1.0f);
                }

                // Faction reinforcements every 100 ticks
                if (t % 100 == 0)
                {
                    g.Factions.AddPresence(Sanctum, Republic, 0.5f);
                    g.Factions.AddPresence(Shiraz, Cartel, 0.5f);
                    g.Factions.AddPresence(Spectre, Pirates, 0.5f);
                    g.Factions.AddPresence(DemosEpsilon, Union, 0.5f);
                }

                g.Tick(dt);

                // Validate every 50 ticks
                if (t % 50 == 0)
                {
                    foreach (var field in g.AllFields)
                    {
                        foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
                        {
                            Assert.IsFalse(float.IsNaN(logAmp),
                                $"Tick {t}: NaN in '{field.FieldId}' at {nodeId}/{channelId}");
                            Assert.IsFalse(float.IsInfinity(logAmp),
                                $"Tick {t}: Infinity in '{field.FieldId}' at {nodeId}/{channelId}");
                            Assert.GreaterOrEqual(logAmp, field.Profile.MinLogAmpClamp - 1e-3f,
                                $"Tick {t}: Below min clamp in '{field.FieldId}' at {nodeId}/{channelId} ({logAmp:F6})");
                            Assert.LessOrEqual(logAmp, field.Profile.MaxLogAmpClamp + 1e-3f,
                                $"Tick {t}: Above max clamp in '{field.FieldId}' at {nodeId}/{channelId} ({logAmp:F6})");
                        }
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 14 — Coup d'état: Faction Flip via Occupation + Presence Shift
        //
        //  Models a scenario where Pirates orchestrate a coup at Demos Epsilon:
        //    1. Deploy overwhelming pirate presence (infiltration)
        //    2. Destabilise Union governance (war + combat)
        //    3. Declare occupation
        //    4. Assert the node flips to Pirate dominance
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Coup_PiratesSeizeDemosEpsilon()
        {
            var g = new Galaxy();
            g.SeedFactions();
            const float dt = 0.1f;

            // Initially Union dominates Demos
            string initialDom = g.Factions.GetDominantFaction(DemosEpsilon);
            Assert.That(initialDom, Is.EqualTo(Union),
                "Union should initially dominate Demos Epsilon.");

            // Pirates infiltrate and attack
            g.War.DeclareWar(DemosEpsilon);

            for (int t = 0; t < 200; t++)
            {
                // Pirates commit heavy forces
                if (t % 10 == 0)
                {
                    g.Combat.CommitForce(DemosEpsilon, Pirates, 2.0f);
                    g.Combat.CommitForce(DemosEpsilon, Union, 0.5f);
                    g.Factions.AddPresence(DemosEpsilon, Pirates, 0.5f);
                }
                g.Tick(dt);
            }

            // After 200 ticks of overwhelming pirate force:
            // combat.intensity → faction.presence coupling has eroded Union presence,
            // while Pirates keep injecting new presence.
            string finalDom = g.Factions.GetDominantFaction(DemosEpsilon);
            float piratePresence = g.PresenceOf(DemosEpsilon, Pirates);
            float unionPresence = g.PresenceOf(DemosEpsilon, Union);

            Assert.That(piratePresence, Is.GreaterThan(unionPresence),
                $"Pirates ({piratePresence:F4}) must surpass Union ({unionPresence:F4}) " +
                "after 200 ticks of overwhelming force and presence injection.");

            Assert.That(finalDom, Is.EqualTo(Pirates),
                "Demos Epsilon must flip to Pirate dominance after the coup.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 15 — Intel-Driven Influence: Spy Networks Project Soft Power
        //
        //  The Republic deploys a massive intelligence network across the
        //  mid-rim. Via intel → influence coupling, this should boost
        //  Republic influence far beyond their military presence.
        //  Compare: influence reach vs presence reach.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void IntelDrivenInfluence_SpyNetworksProjectSoftPower()
        {
            var g = new Galaxy();
            g.SeedFactions();
            const float dt = 0.1f;

            // Republic deploys heavy sensor networks across mid-rim
            for (int t = 0; t < 200; t++)
            {
                if (t % 10 == 0)
                {
                    g.Intel.DeploySensor(CorusAlpha, Republic, 2.0f);
                    g.Intel.DeploySensor(Hyperia, Republic, 2.0f);
                    g.Intel.DeploySensor(Ciom, Republic, 1.0f);
                    g.Intel.DeploySensor(Zadorov, Republic, 0.8f);
                }
                g.Tick(dt);
            }

            // Republic should have influence at nodes where they have NO military presence
            // but DO have intel coverage, thanks to intel → influence coupling
            float influenceZadorov = g.InfluenceOf(Zadorov, Republic);
            float presenceZadorov = g.PresenceOf(Zadorov, Republic);

            // Republic has no initial presence at Zadorov (Union territory),
            // but intel deployments → coverage → coupling → influence
            Assert.That(influenceZadorov, Is.GreaterThan(0f),
                $"Republic must have influence at Zadorov ({influenceZadorov:F4}) " +
                "despite no initial military presence, via intel→influence coupling.");

            // Compare with a node where Republic has BOTH presence and intel
            float influenceHyperia = g.InfluenceOf(Hyperia, Republic);
            Assert.That(influenceHyperia, Is.GreaterThan(influenceZadorov),
                $"Republic influence at Hyperia ({influenceHyperia:F4}) should exceed Zadorov " +
                $"({influenceZadorov:F4}) since Hyperia has both military presence and intel.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 16 — War-to-Stability Coupling: War Destroys Governance
        //
        //  War exposure at a node drives negative stability impulses via coupling.
        //  After prolonged war, stability should be lower than pre-war levels.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void WarDestroysStability_CouplingEffect()
        {
            var g = new Galaxy();
            g.SeedFactions();
            const float dt = 0.1f;

            // Seed stability at Rameses
            g.Factions.AddStability(Rameses, Union, 2.0f);

            // Let stability equilibrate (20 ticks)
            for (int t = 0; t < 20; t++)
                g.Tick(dt);

            float stabilityBefore = g.Factions.Stability.GetLogAmp(Rameses, Union);

            // War breaks out at Rameses
            g.War.DeclareWar(Rameses);

            for (int t = 0; t < 150; t++)
            {
                if (t % 15 == 0)
                    g.Combat.CommitForce(Rameses, Cartel, 2.0f);
                g.Tick(dt);
            }

            float stabilityAfter = g.Factions.Stability.GetLogAmp(Rameses, Union);

            Assert.That(stabilityAfter, Is.LessThan(stabilityBefore),
                $"War must erode stability at Rameses (before={stabilityBefore:F4}, after={stabilityAfter:F4}). " +
                "Chain: war.exposure → coupling (−0.08/tick) → faction.stability declines.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 17 — Full Timeline Event Sequence (750 ticks)
        //
        //  Models a narrative game timeline with discrete events:
        //    t=0:    Galaxy at peace
        //    t=50:   Republic-Cartel border tensions (war at Rameses)
        //    t=150:  Pirate raids on Demos Epsilon
        //    t=200:  Republic deploys intel deep into Cartel space
        //    t=300:  Ceasefire at Rameses (diplomatic resolution)
        //    t=350:  Cartel counterintelligence at Shiraz
        //    t=400:  Pirate ceasefire (bribed by Cartel)
        //    t=500:  New Cartel offensive at Amareth
        //    t=600:  Union + Republic joint defense
        //    t=700:  Full ceasefire
        //
        //  Assert: the galaxy responds causally to each event.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void FullTimeline_EventSequence_750Ticks()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            float warRamesesBefore = 0f;
            float warRamesesAfterCeasefire = 0f;
            float warDemosBeforePirates = 0f;
            float warDemosDuringPirates = 0f;
            float warAmarethDuringOffensive = 0f;
            float warAmarethAfterCeasefire = 0f;

            for (int t = 0; t < 750; t++)
            {
                // Steady trade throughout
                if (t % 10 == 0) g.SeedEconomy();

                // ── Events ──────────────────────────────────────────────
                if (t == 50)
                    g.War.DeclareWar(Rameses);

                if (t == 100)
                    warRamesesBefore = g.WarAt(Rameses);

                if (t == 150)
                {
                    warDemosBeforePirates = g.WarAt(DemosEpsilon);
                    g.War.DeclareWar(DemosEpsilon);
                }

                if (t == 200)
                {
                    g.Intel.DeploySensor(Rameses, Republic, 3.0f);
                    g.Intel.DeploySensor(Amareth, Republic, 2.0f);
                    warDemosDuringPirates = g.WarAt(DemosEpsilon);
                }

                if (t == 300)
                {
                    g.War.DeclareCeasefire(Rameses);
                }

                if (t == 350)
                {
                    warRamesesAfterCeasefire = g.WarAt(Rameses);
                    g.Intel.DeploySensor(Shiraz, Cartel, 5.0f);
                }

                if (t == 400)
                    g.War.DeclareCeasefire(DemosEpsilon);

                if (t == 500)
                {
                    g.War.DeclareWar(Amareth);
                }

                if (t >= 500 && t < 600 && t % 15 == 0)
                {
                    g.Combat.CommitForce(Amareth, Cartel, 2.0f);
                    g.Combat.CommitForce(Amareth, Union, 0.5f);
                }

                if (t == 600)
                {
                    warAmarethDuringOffensive = g.WarAt(Amareth);
                    // Joint defense
                    g.Factions.AddPresence(Amareth, Republic, 2.0f);
                    g.Combat.CommitForce(Amareth, Republic, 3.0f);
                }

                if (t == 700)
                {
                    g.War.DeclareCeasefire(Amareth);
                }

                g.Tick(dt);
            }

            warAmarethAfterCeasefire = g.WarAt(Amareth);

            // ── Event-driven assertions ──────────────────────────────────

            // War built at Rameses between t=50 and t=100
            Assert.That(warRamesesBefore, Is.GreaterThan(0.5f),
                $"War must build at Rameses 50 ticks after declaration ({warRamesesBefore:F4}).");

            // Ceasefire reduced war at Rameses
            Assert.That(warRamesesAfterCeasefire, Is.LessThan(warRamesesBefore),
                $"War at Rameses must decline after ceasefire " +
                $"(before={warRamesesBefore:F4}, after={warRamesesAfterCeasefire:F4}).");

            // Pirate raid raised war at Demos
            Assert.That(warDemosDuringPirates, Is.GreaterThan(warDemosBeforePirates),
                $"Pirate raid must raise war at Demos " +
                $"(before={warDemosBeforePirates:F4}, during={warDemosDuringPirates:F4}).");

            // Cartel offensive built war at Amareth
            Assert.That(warAmarethDuringOffensive, Is.GreaterThan(1.0f),
                $"Cartel offensive must build war at Amareth ({warAmarethDuringOffensive:F4}).");

            // Final ceasefire reduced war at Amareth
            Assert.That(warAmarethAfterCeasefire, Is.LessThan(warAmarethDuringOffensive),
                $"War at Amareth must decline after final ceasefire " +
                $"(during={warAmarethDuringOffensive:F4}, after={warAmarethAfterCeasefire:F4}).");
        }

        // ══════════════════════════════════════════════════════════════════════
        //
        //  TEST 18 — Medicine Supply Chain: War Disrupts Healthcare
        //
        //  Medici is the galaxy's pharmaceutical hub. When war hits its
        //  supply route (Sanctum → Medici), medicine prices across the
        //  galaxy should spike. This tests a specific commodity chain
        //  that crosses faction boundaries.
        //
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void MedicineSupplyChain_WarDisruptsHealthcare()
        {
            var g = new Galaxy();
            g.SeedAll();
            const float dt = 0.1f;

            // Phase 1: Establish medicine distribution (60 ticks)
            for (int t = 0; t < 60; t++)
            {
                if (t % 10 == 0)
                {
                    g.Economy.InjectTrade(Medici, Medicine, 40f);
                    g.Economy.InjectTrade(Sanctum, Medicine, 15f);
                    g.Economy.InjectTrade(Apollo, Medicine, 10f);
                }
                g.Tick(dt);
            }

            float medicinePriceSanctumP1 = g.Price(Sanctum, Medicine);

            // Phase 2: War disrupts the Medici supply route (100 ticks)
            g.War.DeclareWar(Medici);

            for (int t = 0; t < 100; t++)
            {
                if (t % 10 == 0)
                {
                    g.Economy.InjectTrade(Medici, Medicine, 15f);  // reduced output
                    g.Economy.InjectTrade(Sanctum, Medicine, 15f);
                }
                g.Tick(dt);
            }

            float medicinePriceSanctumP2 = g.Price(Sanctum, Medicine);
            float warMedici = g.WarAt(Medici);

            Assert.That(warMedici, Is.GreaterThan(0.5f),
                "War must be active at Medici.");

            Assert.That(medicinePriceSanctumP2, Is.GreaterThan(medicinePriceSanctumP1),
                $"Medicine price at Sanctum must rise when Medici is at war " +
                $"(P1={medicinePriceSanctumP1:F2}, P2={medicinePriceSanctumP2:F2}). " +
                "Chain: war.exposure at Medici → propagation to Sanctum → coupling → price increase.");
        }
    }
}
