# Odengine – AI Coding Instructions

## What This Project Is

A **deterministic quantum-field simulation core** for Unity games — a complete rewrite of the simulation layer from `~/dev/luna-odyssea`. Game facts (prices, threat, war pressure) are **never stored** — they are derived on observation from underlying scalar fields. All behaviour from luna-odyssea's engines (Economy, War, Faction, Logistics) must be re-expressed here as domain systems on top of `Dimension` + `ScalarField`. This is not a standard Unity ECS project.

## Architecture

```
Dimension                      ← top-level container (zero Unity dependencies)
├── NodeGraph                  ← graph topology: Node + Edge (Resistance + Tags)
└── Dictionary<string, ScalarField>
        └── ScalarField        ← (NodeId × ChannelId) → float stored as logAmp
                                  accessed via ChannelView convenience facade
```

**Domain systems** (e.g., `EconomySystem`) are thin wrappers that own one or more named `ScalarField`s and expose domain-meaningful APIs on top of raw field operations. See `EconomySystem.cs` as the canonical pattern.

## Naming Conventions — Critical

- **No `Od` prefix anywhere.** The old luna-odyssea project used `OdNode`, `OdNodeGraph`, `OdWorld` etc. This project dropped that prefix entirely. Types are `Node`, `NodeGraph`, `Dimension`.
- **Neutral concepts in core code.** Core uses `nodeId`, `channelId`, `fieldId` — never "planet", "ship", or "market". Tests may and should use domain names (e.g., `"earth"`, `"water"`, `"faction_a"`) to make intent clear.
- `itemId` / `commodityId` maps to `channelId`. A field like `economy.availability` has thousands of potential channels (`"water"`, `"ore"`, `"rifles"`) — only realized ones are stored.
- **`FieldProfile` is a plain `[Serializable]` C# data class.** Do not make it a `ScriptableObject` — that may come later.

## Semantic ID Policy — Critical

**Odengine never ships semantic IDs.** Strings like `"water"`, `"planet"`, `"ocean"`, `"empire"` are **game content** and belong in the game's bootstrap layer, not in any Odengine assembly. `FieldId`, `ChannelId`, and edge tags are opaque strings the engine treats with no embedded meaning.

- ✅ Engine code: `field.AddLogAmp(nodeId, channelId, delta)` — `channelId` is whatever the game passes
- ✅ Tests: `"economy.availability"`, `"water"`, `"faction_a"` are fine (tests are not shipped core)
- ❌ Core: no `const string WaterChannel = "water"` or similar in `Odengine` namespace

## Critical: Log-Space Storage

`ScalarField` stores values as **log-amplitude** (`logAmp`). Neutral baseline: `logAmp = 0` → `multiplier = 1.0`. Missing dictionary entries are always neutral (sparse).

- Read: `field.GetMultiplier(nodeId, channelId)` → `exp(logAmp)`
- Write: `field.SetLogAmp(...)` or `field.AddLogAmp(...)` (accumulates)
- Never store absolute counts — store log-scale influence

```csharp
// Correct: trade effect in log-space
economy.Availability.AddLogAmp(nodeId, itemId, -availK * units);

// Wrong: this is a log-amp, not "50 units"
field.SetLogAmp(nodeId, itemId, 50f);
```

## Sparse Channel Realization (Impulse / Echo Model)

A field has thousands of conceptual channels but only stores entries where `abs(logAmp) > LogEpsilon`. When an **Impulse** (a direct `AddLogAmp` call) pushes a `(nodeId, channelId)` pair above the threshold, it is realized. When it decays back to neutral, `SetLogAmp` removes it automatically.

**Vocabulary — lock these in, no synonyms:**

| Term            | Meaning                                                          | Code form                                   |
| --------------- | ---------------------------------------------------------------- | ------------------------------------------- |
| **Impulse**     | Immediate local field modification                               | `field.AddLogAmp(nodeId, channelId, delta)` |
| **Propagation** | Deterministic spreading across edges each tick                   | `Propagator.Step(...)`                      |
| **Echo**        | Conceptual only — the distributed logAmp state after propagation | No class exists                             |

- ❌ Do NOT create `Intent`, `Wave`, `EchoEngine`, or `WaveObject` in core
- ❌ Do NOT use "Intent" in engine-core code — it implies deferred resolution; effects are immediate
- "Intent" may appear in the **game layer** for player/AI decision modelling only

## Determinism Rules — Never Break

All iteration over nodes, edges, or channels **must use sorted order**:

- `NodeGraph.GetOutEdgesSorted(nodeId)` — edges sorted on insert (Ordinal)
- `NodeGraph.GetNodeIdsSorted()` — Ordinal-sorted IDs
- `ScalarField.GetActiveChannelIdsSorted()` / `GetActiveNodeIdsSortedForChannel()` — Ordinal sort
- `Propagator.Step()` accumulates all deltas first, then applies them sorted by (channelId, nodeId)

**Never use raw `Dictionary.Keys` in simulation logic.**

## Propagation

`Propagator.Step` is **double-buffered**: accumulate deltas locally, then apply all at once. This prevents order-dependent mutation during iteration.

Formula: `transmittedLogAmpDelta = sourceLogAmp × exp(-Resistance × EdgeResistanceScale) × PropagationRate × dt`

Filter edges by tag: `Propagator.Step(dim, field, dt, requiredEdgeTag: "sea")`.

**Logistics is not a separate engine.** Transport constraints are modelled entirely through edge resistance, `FieldProfile.EdgeResistanceScale`, and edge tag filters. A sea-only route is a set of edges tagged `"sea"`. A trade lane is a low-resistance edge. No inventory movement, no `LogisticsEngine`.

## Serialization

Odengine has a **custom binary snapshot system** (magic `ODSN`, little-endian). The `Docs/serialization-design.md` describes an earlier FlatBuffers plan — the actual implementation is the custom binary format in `Assets/Scripts/Odengine/Serialization/`.

**Three snapshot types (`SnapshotType` enum):**

| Type         | Purpose                                                                             |
| ------------ | ----------------------------------------------------------------------------------- |
| `Full`       | Complete field state, self-contained, no system blobs. Use for per-tick recording.  |
| `Delta`      | Sparse diff against a parent Full. Sentinel `logAmp = 0.0f` marks removed entries.  |
| `Checkpoint` | Full + system blobs for every `ISnapshotParticipant`. Required for load-and-resume. |

**String pool** — all `fieldId`, `nodeId`, `channelId`, tag, and system-ID strings are interned into one Ordinal-sorted pool, written once per snapshot. This makes output byte-identical for identical state.

**`ISnapshotParticipant`** — implement when a domain system holds non-field state (private collections, caches) that cannot be re-derived from `ScalarField` data alone. `WarSystem` is the canonical example. `FactionSystem._lastDominant` is reconstructable via `PostLoad()` without a blob — do not over-apply the interface.

**`DeltaIndex`** — records which `(fieldId, nodeId, channelId)` entries changed at each tick tick, enabling postmortem tools to seek without full replay.

**Critical `BuildPool` rule** — `SnapshotWriter.BuildPool` must include strings from **both** the current active entries _and_ the `previousEntries` dictionary. Removed entries' node/channel IDs disappear from the active map but are still referenced when writing sentinel-zero delta entries. Missing pool entries cause `KeyNotFoundException` at write time.

**`EconomySystem.Availability` logAmp is negative after `InjectTrade`** — `InjectTrade` calls `Availability.AddLogAmp(node, item, -availK * units)`. The negative sign is intentional: supply is consumed, pushing availability below neutral. Assert `Less(logAmp, 0f)`, not `Greater`.

## Cross-Field Coupling (Planned)

A small declarative `CouplingRule` system belongs in Odengine (not the game layer) so coupling stays deterministic and testable. A rule reads one field's sampled value at a node and emits an impulse into another field — using only generic math operators (linear, clamp, ratio, threshold). **No semantic IDs are hardcoded in coupling rules; field IDs and channel selectors are provided by the game.**

Channel selectors (planned): `"*"` (all active channels), `"same"` (same channelId as input), `"explicit:[a,b]"` (list).

Do not implement `CouplingRule` until the core field layer + test suite is solid.

## Adding a New Domain System

Engines ported from luna-odyssea: **EconomySystem** ✅, **WarSystem** ✅, **FactionSystem** ✅. Remaining: **Combat**. (Logistics is replaced by propagation + resistance + edge tags — no separate system needed.)

Pattern for each:

1. Create `Assets/Scripts/Odengine/<Domain>/<Domain>System.cs`
2. Constructor accepts `Dimension`; call `dimension.AddField(fieldId, profile)` for each owned field
3. Expose domain methods that call `ScalarField.AddLogAmp` / `GetMultiplier`
4. `[Serializable]` on data classes; no Unity dependencies in core logic
5. Use plain method calls for domain actions — no event bus, no deferred intents

## Test Structure

Tests live in `Assets/Scripts/Tests/Tests/` and run via Unity Test Runner EditMode (_Window → General → Test Runner → EditMode_). No mocking — construct `Dimension` directly.

**Folder layout:**

```
Tests/Core/Graph/          Graph_NodeGraphTests.cs, etc.
Tests/Core/Fields/         Fields_ScalarFieldTests.cs
Tests/Core/Propagation/    Propagation_PropagatorTests.cs
Tests/Modules/Economy/     Economy_EconomyTests.cs
Tests/Determinism/         hash + replay tests
Tests/Fuzz/                randomised stress tests (use DeterministicRng)
Tests/Scenarios/           multi-tick long-horizon runs
Tests/Snapshots/           Snapshot_CoreTests.cs         — Full/Delta/Checkpoint, string pool, DeltaIndex
                           Snapshot_SystemTests.cs       — WarSystem blob, FactionSystem PostLoad
                           Snapshot_IntegrationTests.cs  — multi-system checkpoint + resume
                           Snapshot_DeterminismTests.cs  — byte-identical output, fuzz, 500-tick
```

**Four test tiers (all required when porting behaviour):**

| Tier        | What                                           | Example                              |
| ----------- | ---------------------------------------------- | ------------------------------------ |
| Core        | Field math, propagation, determinism           | `Fields_ScalarFieldTests.cs`         |
| Domain      | Single system API correctness                  | `Economy_EconomyTests.cs`            |
| Integration | Two+ systems interacting                       | War impulse → price rises in Economy |
| Scenario    | Multi-tick run, invariant checker, time series | 500-tick stability with cascades     |

**Determinism tests use a `StateHash` utility** — sort all active field entries by (fieldId, channelId, nodeId), hash `BitConverter.SingleToInt32Bits(logAmp)` values — and assert the same seed + same operations produce an identical hex hash across runs.

**Fuzz tests use a `DeterministicRng`** (xorshift32) seeded by a `uint`. Generate random graphs, injections, and tick sequences. Assert no NaNs, no infinities, and hash-stable across two runs with the same seed.

**Scenario invariants to check each tick:** no `NaN`/`Infinity` logAmps, values within `FieldProfile.MinLogAmpClamp`/`MaxLogAmpClamp`, all multipliers > 0.

## Key File Map

| File                                                                | Role                                                                            |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| `Assets/Scripts/Odengine/Core/Dimension.cs`                         | Top-level container                                                             |
| `Assets/Scripts/Odengine/Fields/ScalarField.cs`                     | Log-space field storage                                                         |
| `Assets/Scripts/Odengine/Fields/FieldProfile.cs`                    | Per-field behaviour config (plain data class)                                   |
| `Assets/Scripts/Odengine/Fields/Propagator.cs`                      | Deterministic double-buffered propagation                                       |
| `Assets/Scripts/Odengine/Fields/ChannelView.cs`                     | Single-channel facade                                                           |
| `Assets/Scripts/Odengine/Graph/NodeGraph.cs`                        | Sorted graph topology                                                           |
| `Assets/Scripts/Odengine/Economy/EconomySystem.cs`                  | Reference domain system                                                         |
| `Assets/Scripts/Odengine/War/WarSystem.cs`                          | War domain system + `ISnapshotParticipant`                                      |
| `Assets/Scripts/Odengine/Faction/FactionSystem.cs`                  | Faction domain system                                                           |
| `Assets/Scripts/Odengine/Serialization/SnapshotWriter.cs`           | Binary snapshot writer (`ODSN` format)                                          |
| `Assets/Scripts/Odengine/Serialization/SnapshotReader.cs`           | Binary snapshot reader                                                          |
| `Assets/Scripts/Odengine/Serialization/ISnapshotParticipant.cs`     | System blob contract                                                            |
| `Assets/Scripts/Odengine/Serialization/DeltaIndex.cs`               | Per-tick change index for postmortem seeks                                      |
| `Assets/Scripts/Odengine/Serialization/SnapshotHeader.cs`           | Snapshot metadata (tick, type, schema version)                                  |
| `Assets/Scripts/Odengine/Serialization/SnapshotConfig.cs`           | Writer/reader configuration                                                     |
| `Assets/Scripts/Tests/Tests/FieldPropagationTests.cs`               | Propagation + determinism patterns                                              |
| `Assets/Scripts/Tests/Tests/EconomyTests.cs`                        | Domain + scenario test patterns                                                 |
| `Assets/Scripts/Tests/Tests/Snapshots/Snapshot_CoreTests.cs`        | Full/Delta/Checkpoint binary round-trip                                         |
| `Assets/Scripts/Tests/Tests/Snapshots/Snapshot_SystemTests.cs`      | WarSystem blob + FactionSystem PostLoad                                         |
| `Assets/Scripts/Tests/Tests/Snapshots/Snapshot_IntegrationTests.cs` | Multi-system checkpoint + resume workflows                                      |
| `Assets/Scripts/Tests/Tests/Snapshots/Snapshot_DeterminismTests.cs` | Byte-identical output + fuzz + 500-tick scenario                                |
| `Docs/Appendices/`                                                  | Detailed design decisions — read before major changes                           |
| `Docs/serialization-design.md`                                      | Serialization design (describes FlatBuffers plan; actual impl is custom binary) |

## What NOT to Migrate from luna-odyssea

- `OdNode`, `OdNodeGraph`, `OdWorld`, `WorldState`, `WorldSim` — replaced by `Dimension`
- `CommodityLedgerComponent`, `MarketComponent`, explicit stock integers — replaced by logAmp channels
- `LogisticsEngine` moving goods between nodes — replaced by propagation + resistance + edge tags
- `Scope.Planet()`, `Scope.System()` scope-prefixed IDs — nodes are plain string IDs
- `FieldStore`, `Observable`, `MeasurementContext`, `MeasurementCache` — the luna-odyssea V2 prototypes; `ScalarField` + derived domain methods replace all of these
- `Intent`/`EngineOutput`/`IEngine` tick contracts — domain systems expose plain methods called directly
- Any `Od`-prefixed type name
