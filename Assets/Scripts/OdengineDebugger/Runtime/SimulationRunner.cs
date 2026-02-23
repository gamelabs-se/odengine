using System.Collections;
using UnityEngine;
using Odengine.Core;
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

    [Header("Economy")]
    [SerializeField] private float _tradeUnitsPerTick = 5f;

    [Header("War")]
    [SerializeField] private float _warImpulsePerTick = 0.3f;

    // ── Runtime ───────────────────────────────────────────────────────────

    private Dimension      _dim;
    private EconomySystem  _economy;
    private WarSystem      _war;
    private FactionSystem  _factions;
    private ulong          _tick;

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
        DimensionProvider.Register(_dim);
        StartCoroutine(TickLoop());
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
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
    }

    // ── Tick loop ─────────────────────────────────────────────────────────

    private IEnumerator TickLoop()
    {
        var wait = new WaitForSeconds(_tickInterval);

        while (true)
        {
            yield return wait;
            DoTick();
        }
    }

    private void DoTick()
    {
        _tick++;
        float dt = _deltaTime;

        // Economy — small steady injection each tick; equilibrium is bounded by decay
        _economy.InjectTrade(Hub,   "ore",   _tradeUnitsPerTick * 0.4f);
        _economy.InjectTrade(South, "water", _tradeUnitsPerTick * 0.6f);

        Propagator.Step(_dim, _economy.Availability,  dt);
        Propagator.Step(_dim, _economy.PricePressure, dt);

        // War — active at north for first 20 ticks, then ceasefire so it decays
        if (_tick == 20)
            _war.DeclareCeasefire(North);
        // Re-ignite briefly every 60 ticks so the wave is visible cycling
        if (_tick % 60 == 30)
            _war.DeclareWar(North);
        if (_tick % 60 == 40)
            _war.DeclareCeasefire(North);

        _war.Tick(dt);

        // Faction — re-inject presence every 10 ticks to show ongoing rivalry
        if (_tick % 10 == 0)
        {
            _factions.AddPresence(Hub,  FactionRed,  0.15f);
            _factions.AddPresence(West, FactionRed,  0.10f);
            _factions.AddPresence(East, FactionBlue, 0.15f);
            _factions.AddPresence(South, FactionBlue, 0.10f);
        }
        _factions.Tick(dt);

        DimensionProvider.NotifyTick(_tick);
    }
}
