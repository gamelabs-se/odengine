using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Odengine.Core;
using Odengine.Coupling;
using Odengine.Economy;
using Odengine.Faction;
using Odengine.Fields;
using Odengine.War;
using OdengineDebugger;

/// <summary>
/// Self-contained demo simulation.
///
/// Builds a small 5-node star-of-nodes world, wires up Economy + War + Faction
/// systems, then ticks continuously so the Field Debugger window shows live data.
///
/// Open  Odengine → Field Debugger  (Cmd/Ctrl+Alt+D) before pressing Play.
///
/// Nothing here is required by Odengine core — it's a pure consumer.
/// </summary>
public sealed class SimulationRunner : MonoBehaviour
{
    [Header("Tick settings")]
    [Tooltip("Seconds of real time per simulation tick.")]
    [SerializeField] private float _tickInterval = 0.25f;

    [Tooltip("dt passed to Propagator.Step each tick.")]
    [SerializeField] private float _deltaTime = 1f;

    // ── Runtime ───────────────────────────────────────────────────────────

    private Dimension              _dim;
    private EconomySystem          _economy;
    private WarSystem              _war;
    private WarConfig              _warConfig;
    private FactionSystem          _factions;
    private List<CouplingRule>     _couplingRules;
    private ulong                  _tick;

    // Node IDs
    private static readonly string Hub    = "hub";
    private static readonly string North  = "north";
    private static readonly string South  = "south";
    private static readonly string East   = "east";
    private static readonly string West   = "west";

    // Faction IDs
    private static readonly string FactionRed  = "red";
    private static readonly string FactionBlue = "blue";

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Start()
    {
        _dim = BuildDimension();
        BootstrapSystems();
        RegisterControls();
        DimensionProvider.Register(_dim);
        StartCoroutine(TickLoop());
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        SimulationControls.Clear();
        DimensionProvider.Unregister();
    }

    // ── World construction ────────────────────────────────────────────────

    private Dimension BuildDimension()
    {
        var dim = new Dimension();

        // Nodes
        dim.AddNode(Hub,   "Hub");
        dim.AddNode(North, "North");
        dim.AddNode(South, "South");
        dim.AddNode(East,  "East");
        dim.AddNode(West,  "West");

        // Spokes from hub (bidirectional)
        float r = 0.4f;
        dim.AddEdge(Hub, North, r);  dim.AddEdge(North, Hub, r);
        dim.AddEdge(Hub, South, r);  dim.AddEdge(South, Hub, r);
        dim.AddEdge(Hub, East,  r);  dim.AddEdge(East,  Hub, r);
        dim.AddEdge(Hub, West,  r);  dim.AddEdge(West,  Hub, r);

        // One cross-link with a tag
        dim.AddEdge(North, East, 0.8f, "sea");
        dim.AddEdge(East, North, 0.8f, "sea");

        return dim;
    }

    // ── System bootstrap ──────────────────────────────────────────────────

    private void BootstrapSystems()
    {
        // ── Economy ───────────────────────────────────────────────────────
        // Stability rule for a bidirectional N-neighbor star:
        //   DecayRate > sqrt(N) * PropagationRate * exp(-resistance)
        // Hub has 4 spokes, exp(-0.5) ≈ 0.607
        //   Need: DecayRate > 2 * 0.04 * 0.607 = 0.049  → 0.25 gives 5× margin
        var econProfile = new FieldProfile("economy.demo")
        {
            PropagationRate     = 0.04f,
            DecayRate           = 0.25f,
            EdgeResistanceScale = 1f,
            MinLogAmpClamp      = -5f,
            MaxLogAmpClamp      =  5f,
            LogEpsilon          = 0.0001f
        };
        _economy = new EconomySystem(_dim, econProfile);

        // Small steady injections — equilibrium ≈ 0.5–1.5 logAmp at hub
        _economy.InjectTrade(Hub,   "ore",   2f);
        _economy.InjectTrade(South, "water", 3f);
        _economy.InjectTrade(East,  "ore",   1f);

        // ── War ───────────────────────────────────────────────────────────
        var warConfig = new WarConfig
        {
            ExposureGrowthRate  = 0.04f,
            AmbientDecayRate    = 0.06f,
            CeasefireDecayRate  = 0.15f
        };
        _warConfig = warConfig;
        var warProfile = new FieldProfile("war.demo")
        {
            PropagationRate     = 0.04f,
            DecayRate           = 0.25f,
            EdgeResistanceScale = 1.5f,
            MinLogAmpClamp      = 0f,
            MaxLogAmpClamp      = 5f,
            LogEpsilon          = 0.0001f
        };
        _war = new WarSystem(_dim, warProfile, warConfig);

        // War starts active at north — DoTick will ceasefire it after 20 ticks
        _war.DeclareWar(North);

        // ── Faction ───────────────────────────────────────────────────────
        var presenceProfile = new FieldProfile("faction.presence.demo")
        {
            PropagationRate     = 0.03f,
            DecayRate           = 0.12f,
            EdgeResistanceScale = 1f,
            MinLogAmpClamp      = -5f,
            MaxLogAmpClamp      =  5f,
            LogEpsilon          = 0.0001f
        };
        var influenceProfile = new FieldProfile("faction.influence.demo")
        {
            PropagationRate     = 0.04f,
            DecayRate           = 0.10f,
            EdgeResistanceScale = 0.8f,
            MinLogAmpClamp      = -5f,
            MaxLogAmpClamp      =  5f,
            LogEpsilon          = 0.0001f
        };
        var stabilityProfile = new FieldProfile("faction.stability.demo")
        {
            PropagationRate     = 0.02f,
            DecayRate           = 0.08f,
            EdgeResistanceScale = 1.5f,
            MinLogAmpClamp      = -5f,
            MaxLogAmpClamp      =  5f,
            LogEpsilon          = 0.0001f
        };
        _factions = new FactionSystem(_dim, presenceProfile, influenceProfile, stabilityProfile);

        // Seed: red holds hub+west+north, blue holds east+south
        _factions.AddPresence(Hub,   FactionRed,  1.5f);
        _factions.AddPresence(West,  FactionRed,  1.2f);
        _factions.AddPresence(North, FactionRed,  0.6f);
        _factions.AddPresence(East,  FactionBlue, 1.5f);
        _factions.AddPresence(South, FactionBlue, 0.9f);

        // ── Coupling rules ────────────────────────────────────────────────
        // Cross-system linkage: War/Faction state feeds back into Economy.
        // All field IDs reference the canonical strings the systems register.
        string warCh = _warConfig.ExposureChannelId; // "x" by default

        _couplingRules = new List<CouplingRule>
        {
            // War exposure → availability drops (conflict disrupts supply chains)
            // At war logAmp≈2, rate≈0.30/tick vs decay 0.25 → equilibrium ≈ -1.2 logAmp
            new CouplingRule("war.exposure", "economy.availability")
            {
                InputChannelSelector  = warCh,
                OutputChannelSelector = "*",
                Operator              = CouplingOperator.Linear(-0.15f),
                ScaleByDeltaTime      = true,
            },

            // War exposure → price pressure rises (scarcity drives prices)
            // Injects into all currently-traded commodities at the node.
            new CouplingRule("war.exposure", "economy.pricePressure")
            {
                InputChannelSelector  = warCh,
                OutputChannelSelector = "*",
                Operator              = CouplingOperator.Linear(0.08f),
                ScaleByDeltaTime      = true,
            },

            // Strong faction presence → slight availability boost (factions secure trade)
            // Uses "*" input so every faction colour contributes independently.
            new CouplingRule("faction.presence", "economy.availability")
            {
                InputChannelSelector  = "*",
                OutputChannelSelector = "*",
                Operator              = CouplingOperator.Linear(0.04f),
                ScaleByDeltaTime      = true,
            },
        };
    }

    // ── Tick loop ─────────────────────────────────────────────────────────

    private IEnumerator TickLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(_tickInterval);
            DoTick();
        }
    }

    private void DoTick()
    {
        _tick++;
        float dt = _deltaTime;

        Propagator.Step(_dim, _economy.Availability,  dt);
        Propagator.Step(_dim, _economy.PricePressure, dt);
        _war.Tick(dt);
        _factions.Tick(dt);

        // Cross-system coupling: reads from post-propagation state, writes impulses.
        CouplingProcessor.Step(_dim, _couplingRules, dt);

        DimensionProvider.NotifyTick(_tick);
    }

    // ── Interactive controls ──────────────────────────────────────────────

    private void RegisterControls()
    {
        SimulationControls.Clear();
        string ch = _warConfig.ExposureChannelId; // "x" by default

        // ── War ──────────────────────────────────────────────────────────
        SimulationControls.RegisterNodeButton("⚔ War", "Ignite War",  "Ignite!",
            id => _war.DeclareWar(id));

        SimulationControls.RegisterNodeButton("⚔ War", "Ceasefire",   "Ceasefire",
            id => _war.DeclareCeasefire(id));

        SimulationControls.RegisterNodeSlider("⚔ War", "Strike", 0.1f, 3f, 0.5f, "logAmp",
            (id, v) => _war.Exposure.AddLogAmp(id, ch, v));

        // ── Economy ───────────────────────────────────────────────────────
        SimulationControls.RegisterNodeSlider("💰 Economy", "Surge Ore",   1f, 30f, 5f, "units",
            (id, v) => _economy.InjectTrade(id, "ore",   v));

        SimulationControls.RegisterNodeSlider("💰 Economy", "Surge Water", 1f, 30f, 5f, "units",
            (id, v) => _economy.InjectTrade(id, "water", v));

        SimulationControls.RegisterNodeSlider("💰 Economy", "Price Shock", 0.1f, 2f, 0.5f, "logAmp",
            (id, v) => _economy.PricePressure.AddLogAmp(id, "ore", v));

        // ── Faction ───────────────────────────────────────────────────────
        SimulationControls.RegisterNodeSlider("🏳 Faction", "Push Red",  0.1f, 2f, 0.5f, "logAmp",
            (id, v) => _factions.AddPresence(id, FactionRed,  v));

        SimulationControls.RegisterNodeSlider("🏳 Faction", "Push Blue", 0.1f, 2f, 0.5f, "logAmp",
            (id, v) => _factions.AddPresence(id, FactionBlue, v));

        SimulationControls.RegisterNodeSlider("🏳 Faction", "Destabilize", 0.1f, 2f, 0.3f, "logAmp",
            (id, v) => {
                _factions.Stability.AddLogAmp(id, FactionRed,  -v);
                _factions.Stability.AddLogAmp(id, FactionBlue, -v);
            });

        // ── Sim ──────────────────────────────────────────────────────────
        SimulationControls.RegisterSlider("⚙ Sim", "Tick Interval", 0.05f, 2f, 0.25f, "s",
            v => _tickInterval = v);

        SimulationControls.RegisterButton("⚙ Sim", "Reset Sim", () =>
        {
            StopAllCoroutines();
            DimensionProvider.Unregister();
            _tick = 0;
            _dim = BuildDimension();
            BootstrapSystems();
            RegisterControls();
            DimensionProvider.Register(_dim);
            StartCoroutine(TickLoop());
        });
    }
}
