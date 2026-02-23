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
        var econProfile = new FieldProfile("economy.demo")
        {
            PropagationRate    = 0.15f,
            DecayRate          = 0.05f,
            EdgeResistanceScale = 1f,
            MinLogAmpClamp     = -10f,
            MaxLogAmpClamp     =  10f,
            LogEpsilon         = 0.0001f
        };
        _economy = new EconomySystem(_dim, econProfile);

        // Seed some initial trade in two commodities
        _economy.InjectTrade(Hub,   "ore",   40f);
        _economy.InjectTrade(South, "water", 60f);
        _economy.InjectTrade(East,  "ore",   20f);

        var warProfile = new FieldProfile("war.demo")
        {
            PropagationRate    = 0.20f,
            DecayRate          = 0.08f,
            EdgeResistanceScale = 1.2f,
            MinLogAmpClamp     = -10f,
            MaxLogAmpClamp     =  10f,
            LogEpsilon         = 0.0001f
        };
        var warConfig = new WarConfig();
        _war = new WarSystem(_dim, warProfile, warConfig);

        // Light war pressure at north; will propagate
        _war.DeclareWar(North);

        var presenceProfile = new FieldProfile("faction.presence.demo")
        {
            PropagationRate     = 0.10f,
            DecayRate           = 0.03f,
            EdgeResistanceScale = 1f,
            MinLogAmpClamp      = -10f,
            MaxLogAmpClamp      =  10f,
            LogEpsilon          = 0.0001f
        };
        var influenceProfile = new FieldProfile("faction.influence.demo")
        {
            PropagationRate     = 0.12f,
            DecayRate           = 0.02f,
            EdgeResistanceScale = 0.8f,
            MinLogAmpClamp      = -10f,
            MaxLogAmpClamp      =  10f,
            LogEpsilon          = 0.0001f
        };
        var stabilityProfile = new FieldProfile("faction.stability.demo")
        {
            PropagationRate     = 0.05f,
            DecayRate           = 0.01f,
            EdgeResistanceScale = 1.5f,
            MinLogAmpClamp      = -10f,
            MaxLogAmpClamp      =  10f,
            LogEpsilon          = 0.0001f
        };
        _factions = new FactionSystem(_dim, presenceProfile, influenceProfile, stabilityProfile);

        // Red dominates hub + west; blue holds east
        _factions.AddPresence(Hub,   FactionRed,  2.0f);
        _factions.AddPresence(West,  FactionRed,  1.5f);
        _factions.AddPresence(North, FactionRed,  0.8f);
        _factions.AddPresence(East,  FactionBlue, 2.0f);
        _factions.AddPresence(South, FactionBlue, 1.0f);
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

        // Economy — inject ongoing trade at hub, then propagate both fields
        _economy.InjectTrade(Hub, "ore",   _tradeUnitsPerTick * 0.5f);
        _economy.InjectTrade(South, "water", _tradeUnitsPerTick);

        Propagator.Step(_dim, _economy.Availability,  dt);
        Propagator.Step(_dim, _economy.PricePressure, dt);

        // War — pulse at north, propagate
        if (_tick % 4 == 0)
            _war.DeclareWar(North);

        _war.Tick(dt);

        // Faction — propagate all three fields
        _factions.Tick(dt);

        // Notify the debugger
        DimensionProvider.NotifyTick(_tick);
    }
}
