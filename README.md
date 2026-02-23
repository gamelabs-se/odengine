# Odengine

**A deterministic scalar-field simulation core for Unity games.**

## What Is This?

Odengine models a game world as a graph of nodes embedded in sparse scalar fields. Game facts — prices, supply, faction dominance, war pressure — are **never stored**. They are derived on observation from the underlying log-amplitude fields, deterministically and repeatably.

### Core Philosophy

- **Fields are primary.** The world is not objects with stats — it is fields with values sampled at nodes.
- **Observation derives, not stores.** A price or inventory count is computed on demand; it does not exist until measured.
- **Determinism is non-negotiable.** Same seed + same operations = identical results, byte-for-byte.
- **Sparse by default.** Only realized (non-neutral) `(nodeId, channelId)` pairs are stored.

## Architecture

```
Dimension                          ← top-level container; zero Unity dependencies
├── NodeGraph                      ← graph topology: Node + Edge (resistance, tags)
└── Dictionary<string, ScalarField>
        └── ScalarField            ← (nodeId × channelId) → float, stored as logAmp
                                      accessed via ChannelView convenience facade
```

**Domain systems** are thin wrappers that own one or more named `ScalarField`s and expose domain-meaningful APIs on top of raw field operations.

| System          | Fields                                                       | Extra non-field state                                            |
| --------------- | ------------------------------------------------------------ | ---------------------------------------------------------------- |
| `EconomySystem` | `economy.availability`, `economy.pricePressure`              | None                                                             |
| `WarSystem`     | `war.exposure`                                               | `_activeWarNodes`, `_coolingNodes`, `_stability`, `_occupations` |
| `FactionSystem` | `faction.presence`, `faction.influence`, `faction.stability` | `_lastDominant` (reconstructable)                                |

## Log-Space Storage

`ScalarField` stores values as **log-amplitude** (`logAmp`). Neutral baseline: `logAmp = 0` → `multiplier = 1.0`. Missing entries are always neutral (sparse dictionary).

```csharp
// Read — returns exp(logAmp)
float mult = field.GetMultiplier(nodeId, channelId);

// Write — accumulates (does not replace)
field.AddLogAmp(nodeId, channelId, -0.1f * units);

// Entries at or below LogEpsilon (0.0001f) are removed automatically
```

## Example Usage

```csharp
// Build simulation
var dim = new Dimension();
dim.Graph.AddNode("earth");
dim.Graph.AddNode("mars");
dim.Graph.AddEdge("earth", "mars", resistance: 0.5f, tags: new[]{"space"});

var economy = new EconomySystem(dim);

// Inject trade — log-space impulse
economy.InjectTrade("earth", "mars", itemId: "ore", units: 10f);

// Propagate availability field along edges
Propagator.Step(dim, economy.Availability, dt: 1f);

// Derive multiplier at destination
float supplyMult = economy.Availability.GetMultiplier("mars", "ore");

// Snapshot the world
var writer = new SnapshotWriter();
byte[] checkpoint = writer.WriteCheckpoint(dim, tick: 1, simTime: 1.0,
    participants: new ISnapshotParticipant[]{ warSystem });

// Restore later
var reader = new SnapshotReader();
var (dim2, header, blobs) = reader.ReadCheckpoint(checkpoint);
warSystem.PostLoad(blobs["war"]);
```

## Propagation

`Propagator.Step` is double-buffered: all deltas are accumulated first, then applied in sorted order. This prevents order-dependent mutation during iteration.

```
transmittedDelta = sourceLogAmp × exp(-resistance × EdgeResistanceScale) × PropagationRate × dt
```

Filter edges by tag: `Propagator.Step(dim, field, dt, requiredEdgeTag: "space")`.

Transport constraints (logistics) are modelled entirely through edge resistance and tag filters — there is no separate `LogisticsEngine`.

## Serialization

Odengine has a **custom binary snapshot system** (`ODSN` magic, little-endian) supporting three snapshot types:

| Type         | Purpose                                                                                                       |
| ------------ | ------------------------------------------------------------------------------------------------------------- |
| `Full`       | Complete field state, self-contained, no system blobs. Use for per-tick recording.                            |
| `Delta`      | Sparse diff against a parent Full. Only changed entries written; sentinel `logAmp = 0` marks removed entries. |
| `Checkpoint` | Full + system blobs for all `ISnapshotParticipant` systems. Required for load-and-resume.                     |

**String pool** — all `fieldId`, `nodeId`, `channelId`, tag, and system-ID strings are interned into an Ordinal-sorted pool written once per snapshot. This makes output byte-identical for identical state.

**`ISnapshotParticipant`** — systems with non-field state (e.g., `WarSystem`) implement this interface to emit/consume opaque versioned byte blobs at Checkpoint save/load time.

**`DeltaIndex`** — records which (fieldId, nodeId, channelId) entries changed at each tick, so postmortem tools can seek to any tick without replaying from the start.

## Key Guarantees

1. **Determinism** — all iteration is Ordinal-sorted; same state always produces identical bytes.
2. **Sparsity** — entries within `LogEpsilon` of neutral are never written; absent entries read as neutral.
3. **Schema evolution** — versioned blobs; new fields/systems do not break loading of older snapshots.
4. **No semantic IDs in core** — `fieldId`, `nodeId`, `channelId` are opaque strings the engine never interprets.

## Tests

Run via _Window → General → Test Runner → EditMode_ in the Unity editor.

```
Tests/Core/Graph/          NodeGraph correctness
Tests/Core/Fields/         ScalarField log-space math
Tests/Core/Propagation/    Propagator double-buffer + determinism
Tests/Modules/Economy/     EconomySystem API
Tests/Determinism/         StateHash byte-identical across runs
Tests/Fuzz/                DeterministicRng randomised stress
Tests/Scenarios/           Multi-tick long-horizon invariant checks
Tests/Snapshots/           Binary round-trip (Full, Delta, Checkpoint, DeltaIndex)
```

## Status

**Branch**: `feature/serialization`

**Complete:**

- `NodeGraph` + `ScalarField` + `Propagator` — core field layer
- `EconomySystem`, `WarSystem`, `FactionSystem` — domain systems
- Binary snapshot system — Full, Delta, Checkpoint, DeltaIndex
- `ISnapshotParticipant` — extensible system-blob contract
- Full test suite: core, domain, integration, determinism, fuzz, scenarios, snapshots

**Planned:**

- `CouplingRule` — declarative cross-field coupling (after test suite is solid)
- Combat system
- Postmortem replay tooling (game layer concern)

## License

MIT (or your choice)
