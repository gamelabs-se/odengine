# Odengine — Binary Serialization Design

### FlatBuffer-based sparse snapshot format

> **Supersedes** `Docs/snapshot-strategy.md`.  
> The earlier document recommended custom binary first and FlatBuffers later.  
> This document supersedes that plan. FlatBuffers is the right answer now because
> the schema — once settled — is stable, the Unity FlatBuffers C# runtime is a
> single file with no managed allocations on the read path, and the schema
> compiler enforces forward/backward compatibility automatically. The extra
> tooling cost is worth avoiding a bespoke binary parser we'd eventually rewrite anyway.

---

## Table of Contents

1. [Goals and non-goals](#1-goals-and-non-goals)
2. [What is saved and what is not](#2-what-is-saved-and-what-is-not)
3. [FlatBuffer schema](#3-flatbuffer-schema)
4. [String pool — ID interning](#4-string-pool--id-interning)
5. [Sparsity — the LogEpsilon threshold](#5-sparsity--the-logepsilon-threshold)
6. [Snapshot types: Full, Delta, Checkpoint](#6-snapshot-types-full-delta-checkpoint)
7. [Load modes: analysis vs resume](#7-load-modes-analysis-vs-resume)
8. [ISnapshotParticipant — the system serialization contract](#8-isnapshotparticipant--the-system-serialization-contract)
9. [Configuration](#9-configuration)
10. [Integration in the current codebase](#10-integration-in-the-current-codebase)
11. [Design contract going forwards](#11-design-contract-going-forwards)
12. [Game-layer usage patterns](#12-game-layer-usage-patterns)
13. [Edge cases](#13-edge-cases)
14. [Test strategy](#14-test-strategy)
15. [Tooling and build integration](#15-tooling-and-build-integration)

---

## 1. Goals and non-goals

### Goals

- **Save entire simulation runs** in a compact, seekable binary format suitable for postmortem analysis.
- **Save/load/resume** — both load-for-analysis (fields only) and full state restoration (fields + system-specific state).
- **Sparse by default** — never write logAmp entries within `LogEpsilon` (0.0001f) of neutral. A field with 50,000 conceptual channels that has only 40 active ones should occupy space proportional to those 40 entries.
- **Deterministic byte output** — same Dimension state, same tick number → identical bytes. Enables hash-based regression testing.
- **Schema evolution** — adding a new field or system must not break loading of old snapshots.
- **Performance** — write and read for realistic game worlds (see §14 for benchmarks) must complete in under 16ms for a full snapshot, sub-ms for deltas.
- **Zero game-layer coupling** — Odengine does not know about game-specific content. The serialization layer treats fieldIds, channelIds, and nodeIds as opaque strings.

### Non-goals

- Cross-session cloud sync. That is a game concern.
- Compression. The raw FlatBuffer output can be piped through LZ4/Zstd at the game layer. Odengine does not bundle a compression library.
- Replay playback UI. Odengine provides the data format; visualization is game-layer.
- Unity-serialized assets (ScriptableObjects). This is a pure C# format loadable outside Unity.

---

## 2. What is saved and what is not

### Saved

| Layer                   | Contents                                                                                                                                                                    |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ | ----------------------------------------------------------------------- |
| **Graph topology**      | All `Node` records (id, name) and `Edge` records (fromId, toId, resistance, tags[]). Stored in every Full snapshot; omitted from Deltas.                                    |
| **Field registrations** | For each `ScalarField`: its `fieldId` and all `FieldProfile` properties. Allows reconstructing a Dimension with the correct profile even if the active entry count is zero. |
| **Field data (sparse)** | For each `ScalarField`: the set of `(nodeId, channelId, logAmp)` tuples where `                                                                                             | logAmp | > LogEpsilon`. This is exactly the contents of `ScalarField.\_logAmps`. |
| **System blobs**        | Opaque byte arrays emitted by `ISnapshotParticipant` implementors. Versioned per system. See §8.                                                                            |
| **Snapshot metadata**   | Tick counter, sim time, creation timestamp, schema version, snapshot type (Full/Delta/Checkpoint).                                                                          |

### Not saved / reconstructed on load

| Data                                                 | Why omitted                                                         | How restored                                                                            |
| ---------------------------------------------------- | ------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `FactionSystem._lastDominant`                        | Callback-diffing cache only — not simulation truth                  | Reconstructed by calling `GetDominantChannel` for every active node after field restore |
| Derived quantities (prices, dominance, is-contested) | Computed on observation; they are never stored even in memory       | Re-derived from fields post-load                                                        |
| Unity-side `MonoBehaviour` state                     | Out of scope; game layer manages this                               | Game layer handles it                                                                   |
| `ScalarField` entries at or below `LogEpsilon`       | Equivalent to neutral (multiplier ≈ 1.0); omitting them is lossless | Absent entries default to 0 logAmp in `GetLogAmp`                                       |

### Systems and their extra-state requirements

| System          | Fields registered                                            | Extra non-field state                                            | Must implement `ISnapshotParticipant`? |
| --------------- | ------------------------------------------------------------ | ---------------------------------------------------------------- | -------------------------------------- |
| `EconomySystem` | `economy.availability`, `economy.pricePressure`              | None                                                             | No                                     |
| `WarSystem`     | `war.exposure`                                               | `_activeWarNodes`, `_coolingNodes`, `_stability`, `_occupations` | **Yes**                                |
| `FactionSystem` | `faction.presence`, `faction.influence`, `faction.stability` | `_lastDominant` (reconstructable)                                | No                                     |
| Future systems  | TBD                                                          | Only if they hold non-field state                                | Yes if so                              |

> **Principle**: if a system stores state in `ScalarField` it is free. If it stores state in private
> collections (HashSets, Dictionaries) that cannot be re-derived from field data, it must implement
> `ISnapshotParticipant`. The interface is the forcing function that makes new system authors think
> about which state is real vs. derived.

---

## 3. FlatBuffer schema

File: `Assets/Scripts/Odengine/Serialization/schema/odengine_snapshot.fbs`

```flatbuffers
// odengine_snapshot.fbs
// Odengine FlatBuffer serialization schema.
// NEVER reorder or delete existing fields — only add new fields at the END
// of each table to preserve forward/backward compatibility.

namespace Odengine.Serialization.FlatBuffers;

// ── String pool ────────────────────────────────────────────────────────────
// All nodeIds, channelIds, fieldIds, tagIds, and systemIds are stored
// as uint32 indices into this pool rather than inline strings.
// The pool is sorted Ordinally before writing for determinism.

table StringPool {
  strings: [string];  // index 0 = empty string sentinel
}

// ── Graph ─────────────────────────────────────────────────────────────────

table NodeRecord {
  id_idx:   uint32;   // index into StringPool
  name_idx: uint32;   // index into StringPool; same as id_idx if name == id
}

table EdgeRecord {
  from_idx:   uint32;   // index into StringPool
  to_idx:     uint32;
  resistance: float;
  tag_idxs:   [uint32]; // indices into StringPool, sorted Ordinally
}

table GraphSnapshot {
  nodes: [NodeRecord];
  edges: [EdgeRecord];
}

// ── Fields ────────────────────────────────────────────────────────────────

table FieldProfileRecord {
  profile_id_idx:        uint32;
  propagation_rate:      float = 1.0;
  edge_resistance_scale: float = 1.0;
  decay_rate:            float = 0.0;
  min_log_amp_clamp:     float = -20.0;
  max_log_amp_clamp:     float = 20.0;
  log_epsilon:           float = 0.0001;  // sparsity pruning threshold — must match FieldProfile.LogEpsilon
}

// A single sparse (nodeId, channelId, logAmp) entry.
// Using uint32 indices keeps this to 12 bytes flat.
table FieldEntry {
  node_idx:    uint32;
  channel_idx: uint32;
  log_amp:     float;
}

// A delta entry records both old and new logAmp so a reader can verify
// continuity and apply the change without needing the previous full snapshot.
// For entries new since the last snapshot, old_log_amp == 0.
// For entries removed since the last snapshot, new_log_amp == 0.
table FieldDeltaEntry {
  node_idx:    uint32;
  channel_idx: uint32;
  new_log_amp: float;
}

table FieldSnapshot {
  field_id_idx: uint32;
  profile:      FieldProfileRecord;
  entries:      [FieldEntry];    // used in Full/Checkpoint snapshots (sorted)
  delta_entries:[FieldDeltaEntry]; // used in Delta snapshots only
}

// ── System blobs ──────────────────────────────────────────────────────────

table SystemBlob {
  system_id_idx:  uint32;   // index into StringPool
  schema_version: uint16;   // per-system blob version (start at 1)
  payload:        [ubyte];  // system-defined; must be deterministic
}

// ── Header ────────────────────────────────────────────────────────────────

enum SnapshotType : byte {
  Full       = 0,  // complete state; self-contained
  Delta      = 1,  // only changes since parent_tick
  Checkpoint = 2   // Full + all system blobs; fully resumable
}

table SnapshotHeader {
  magic:             uint32 = 0x4F44534E;  // 'ODSN'
  schema_version:    uint16 = 1;
  snapshot_type:     SnapshotType;
  tick:              uint64;
  sim_time:          float64;
  created_utc_ms:    uint64;   // Unix epoch milliseconds
  parent_tick:       uint64;   // 0 if Full/Checkpoint; tick of parent for Delta
  delta_chain_depth: uint16;   // 0 for Full/Checkpoint; depth from last Full
  engine_version:    string;   // semver string e.g. "0.4.1"
}

// ── Root ──────────────────────────────────────────────────────────────────

table Snapshot {
  header:       SnapshotHeader (required);
  pool:         StringPool     (required);
  graph:        GraphSnapshot;           // null in Delta snapshots
  fields:       [FieldSnapshot];
  system_blobs: [SystemBlob];            // null unless Checkpoint
}

root_type Snapshot;
file_identifier "ODSN";
file_extension "odsn";
```

### Schema evolution rules

1. **Never** reorder or remove fields from an existing table. FlatBuffers tables are accessed by field offset, not position.
2. **Always** add new fields at the **end** of a table with a sensible default.
3. When the semantics of a field change incompatibly (not just extension), bump `SnapshotHeader.schema_version` and add a migration path in `SnapshotReader`.
4. Per-system schema is versioned separately via `SystemBlob.schema_version`. Each system is responsible for its own migration.
5. The `file_identifier` acts as a magic-bytes guard. Load will refuse files that don't start with `ODSN`.

---

## 4. String pool — ID interning

FlatBuffers does **not** automatically deduplicate strings across a buffer. Without interning, a field with 10,000 entries all sharing the same 20 channelIds would write that channelId string 10,000 times. With interning, each unique string is written once.

### Pool construction algorithm

```
// During SnapshotWriter.WriteFull():
1. Collect every string that will appear in the buffer:
   - All nodeIds from NodeGraph
   - All channelIds from every active field entry
   - All fieldIds
   - All edge tagIds
   - All systemIds
   - All profileIds
2. Add "" at index 0 (sentinel for optional fields that have no value).
3. Sort the collected strings Ordinally (required for determinism).
4. Build a Dictionary<string, uint32> for O(1) lookup during encoding.
5. Write the pool as the first table in the buffer.
```

### Numeric impact

A typical mid-game world: 500 nodes, 20 fields, 200 unique channelIds, 5 unique tags = **~725 unique strings**. The pool is ~15KB. Each entry in the field data costs 12 bytes (2 × uint32 + float) instead of a variable-length string pair. At 100,000 active entries, pool interning saves several megabytes.

---

## 5. Sparsity — the LogEpsilon threshold

`FieldProfile.LogEpsilon` (default `0.0001f`) controls sparsity: any `SetLogAmp` call
that would leave `|logAmp| < Profile.LogEpsilon` removes the entry from `_logAmps`.
The `Propagator` uses `field.Profile.LogEpsilon` for its skip checks too, so all three
sites (`SetLogAmp`, propagation source skip, transmission delta skip) track the same value.

**Previously** this was `private const float LogEpsilon = 0.0001f` baked into `ScalarField` —
a compile-time constant invisible to serialization. It is now a `FieldProfile` property,
which means it is stored per-field in `FieldProfileRecord.log_epsilon` and restored on load.
A simulation that was run with `LogEpsilon = 0.00001f` (tighter precision) will load and
resume with exactly that threshold — no divergence.

Because `_logAmps.Keys` is exactly the set of entries that survived the threshold check,
the snapshot write path is trivially correct:

```csharp
// SnapshotWriter: still trivially correct
foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
    builder.AddFieldEntry(pool[nodeId], pool[channelId], logAmp);
```

On read, absent entries default to `logAmp = 0f`. Loading is lossless for all values
that were above the stored threshold and exactly neutral for everything below it.

### Different fields can have different thresholds

A high-precision economic field might use `LogEpsilon = 0.000001f`; a coarse war-exposure
field might use `0.001f`. Both are stored and restored independently. The `Propagator`
reads `field.Profile.LogEpsilon` at call time, so precision is always per-field-correct.

---

## 6. Snapshot types: Full, Delta, Checkpoint

### Full snapshot

Contains: header, pool, graph, all field data.  
Does **not** contain system blobs.  
Use for: periodic checkpoints during a run, postmortem analysis frames.

Size characteristics: dominated by field entry count. At 100K active entries, expect ~1.5MB raw (before any platform compression).

### Delta snapshot

Contains: header (with `parent_tick` set), pool, field **delta** only — entries added, changed, or removed since the parent snapshot.  
Does **not** contain graph or system blobs.  
The pool covers only the strings referenced by the delta.

A delta entry with `new_log_amp == 0` means the entry was removed (decayed to neutral) since the parent.

**Delta chain**: a sequence of Full → Delta → Delta → ... → Delta. The reader walks the chain from the Full forward, applying deltas in order, to reconstruct any intermediate tick. After `DeltaChainMaxLength` deltas (configurable, default 100), `SnapshotWriter` automatically emits a new Full to restart the chain and keep seek costs bounded.

To reconstruct tick T:

1. Find the most recent Full at or before T.
2. Apply each Delta in order from that Full through T.

### Checkpoint snapshot

Contains: header (`SnapshotType.Checkpoint`), pool, graph, all field data, **and** all system blobs from registered `ISnapshotParticipant`s.  
This is the only snapshot type that supports full state restoration and simulation resume.

Use for: game save slots, crash recovery, cross-session resume.

Checkpoints should be written explicitly by the game layer at save points rather than automatically each tick — they are heavier to write (system blob serialization) and semantically richer (resumable).

---

## 7. Load modes: analysis vs resume

### Load for analysis

Reconstructs `Dimension` (graph + fields) from any snapshot type (Full, Delta after chain traversal, or Checkpoint). System blobs are ignored. No domain systems are constructed.

```csharp
var reader = new SnapshotReader();
SnapshotData snap = reader.ReadFull(bytes);          // or ReadAtTick(series, tick)
Dimension dim = snap.ReconstructDimension();

// Inspect freely:
float price = dim.GetField("economy.availability").GetMultiplier("earth", "water");
string dominant = dim.GetField("faction.presence").GetDominantChannel("titan");
```

The returned `Dimension` is fully functional. All field operations (`GetLogAmp`, `GetMultiplier`, `EnumerateAllActiveSorted`) work normally. No tick calls are safe unless you also restore systems.

### Load and resume

Reconstructs `Dimension`, then restores system-specific state by calling `ISnapshotParticipant.DeserializeSystemState` on each registered system. After this call, `Tick()` may be called normally.

**Restoration order** is fixed and must be documented per project:

1. Reconstruct `Dimension` (graph + fields).
2. Construct systems in canonical order (same order as original run).
3. For each registered participant, call `snap.RestoreSystem(participant)`.
4. Call post-load hooks (`ISnapshotParticipant.PostLoad`) so that reconstructable caches (e.g., `FactionSystem._lastDominant`) are rebuilt.
5. Resume ticking.

```csharp
// Load and resume example (game layer)
SnapshotData snap = reader.ReadCheckpoint(bytes);

var dim = new Dimension();
snap.RestoreGraph(dim);
snap.RestoreFields(dim);

var war     = new WarSystem(dim, exposureProfile);
var factions = new FactionSystem(dim, presenceProfile, influenceProfile, stabilityProfile);
var economy  = new EconomySystem(dim, economyProfile);

snap.RestoreSystem(war);     // reads SystemBlob for "war.system"
// factions and economy have no blob — PostLoad rebuilds _lastDominant
factions.PostLoad();

// dim, war, factions, economy are now in the saved state
// Continue ticking:
for (int i = 0; i < 100; i++)
{
    war.Tick(1f);
    factions.Tick(1f);
}
```

### Validation on load

`SnapshotReader` validates:

- `file_identifier == "ODSN"` — wrong format.
- `SnapshotHeader.magic == 0x4F44534E` — corrupt file.
- `SnapshotHeader.schema_version <= CurrentSchemaVersion` — too new to load.
- `SnapshotType == Checkpoint` is required for resume mode; non-checkpoint load-and-resume throws `InvalidOperationException`.
- Graph node references in field entries refer to nodes that exist in the graph. (Validation mode only; skippable in prod for performance.)

---

### System-level tuning constants

Systems like `WarSystem` currently hold tuning constants as `private const` values
(`ExposureGrowthRate`, `AmbientDecayRate`, `CeasefireDecayRate`, `OccupationBaseRate`,
`OccupationStabilityResist`). These have the same problem `LogEpsilon` had: if you
tune them between a save and a load, the resumed simulation diverges.

**Required pattern for any system with tuning constants:**

1. Extract constants into a dedicated `*Config` or `*Profile` data class:
   ```csharp
   public sealed class WarConfig
   {
       public float ExposureGrowthRate    { get; set; } = 0.05f;
       public float AmbientDecayRate      { get; set; } = 0.02f;
       public float CeasefireDecayRate    { get; set; } = 0.06f;
       public float OccupationBaseRate    { get; set; } = 0.1f;
       public float OccupationStabilityResist { get; set; } = 0.2f;
   }
   ```
2. Pass the config to the system constructor. The system stores it.
3. Serialize the config inside `SerializeSystemState()` (versioned, sorted).
4. Restore it inside `DeserializeSystemState()` before any Tick is called.

This is tracked as implementation work; `WarSystem` is the first system that needs it.
Until it is done, changing `WarSystem`'s private constants between save and resume will
cause silent divergence — treat those constants as frozen until the config struct lands.

---



```csharp
// Assets/Scripts/Odengine/Serialization/ISnapshotParticipant.cs
namespace Odengine.Serialization
{
    /// <summary>
    /// Implement this interface on any domain system that holds state that:
    ///   (a) cannot be reconstructed from its ScalarField entries alone, AND
    ///   (b) must be preserved across a save/load cycle for simulation to resume correctly.
    ///
    /// Systems whose state is entirely in ScalarFields (e.g. EconomySystem) do NOT
    /// need to implement this — their state is captured by the field serialization pass.
    ///
    /// Systems with reconstructable caches (e.g. FactionSystem._lastDominant) should NOT
    /// implement this either — implement PostLoad() to rebuild the cache from fields.
    /// </summary>
    public interface ISnapshotParticipant
    {
        /// <summary>
        /// Stable identifier for this system's blob in the snapshot.
        /// Must be unique across all participants. Must never change once committed.
        /// Convention: "namespace.systemname", e.g. "war.system".
        /// </summary>
        string SystemId { get; }

        /// <summary>
        /// Serialize all non-field system state to a byte array.
        /// Output must be deterministic: same state → identical bytes.
        /// Version the layout via the first byte(s) of the payload.
        /// </summary>
        byte[] SerializeSystemState();

        /// <summary>
        /// Restore non-field system state from a blob written by a previous
        /// SerializeSystemState() call. Called after field restoration; the Dimension
        /// and all fields are in their saved state when this is called.
        /// </summary>
        /// <param name="payload">Exact bytes from SystemBlob.payload.</param>
        /// <param name="blobSchemaVersion">SystemBlob.schema_version from the file.
        ///   Use to apply migrations when loading old blobs.</param>
        void DeserializeSystemState(byte[] payload, int blobSchemaVersion);

        /// <summary>
        /// Called after all fields and system blobs have been restored.
        /// Use to rebuild any in-memory caches that can be derived from field state.
        /// Default implementation does nothing.
        /// </summary>
        void PostLoad() { }
    }
}
```

### WarSystem implementation sketch

```csharp
// WarSystem partial implementation of ISnapshotParticipant
public string SystemId => "war.system";

public byte[] SerializeSystemState()
{
    // Simple custom binary layout — WarSystem owns its own blob format.
    // Sorted for determinism.
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);

    w.Write((byte)1); // blob schema version

    // _activeWarNodes (sorted)
    var activeList = new List<string>(_activeWarNodes);
    activeList.Sort(StringComparer.Ordinal);
    w.Write(activeList.Count);
    foreach (var n in activeList) w.Write(n);

    // _coolingNodes (sorted)
    var coolingList = new List<string>(_coolingNodes);
    coolingList.Sort(StringComparer.Ordinal);
    w.Write(coolingList.Count);
    foreach (var n in coolingList) w.Write(n);

    // _stability (sorted by key)
    var stabKeys = new List<string>(_stability.Keys);
    stabKeys.Sort(StringComparer.Ordinal);
    w.Write(stabKeys.Count);
    foreach (var k in stabKeys) { w.Write(k); w.Write(_stability[k]); }

    // _occupations (sorted by node key)
    var occKeys = new List<string>(_occupations.Keys);
    occKeys.Sort(StringComparer.Ordinal);
    w.Write(occKeys.Count);
    foreach (var k in occKeys)
    {
        var (attackerId, progress) = _occupations[k];
        w.Write(k); w.Write(attackerId); w.Write(progress);
    }

    return ms.ToArray();
}

public void DeserializeSystemState(byte[] payload, int blobSchemaVersion)
{
    using var ms = new MemoryStream(payload);
    using var r = new BinaryReader(ms);

    byte version = r.ReadByte();
    if (version != 1)
        throw new NotSupportedException($"WarSystem blob v{version} not supported by this build");

    int activeCount = r.ReadInt32();
    _activeWarNodes.Clear();
    for (int i = 0; i < activeCount; i++) _activeWarNodes.Add(r.ReadString());

    int coolingCount = r.ReadInt32();
    _coolingNodes.Clear();
    for (int i = 0; i < coolingCount; i++) _coolingNodes.Add(r.ReadString());

    int stabCount = r.ReadInt32();
    _stability.Clear();
    for (int i = 0; i < stabCount; i++) _stability[r.ReadString()] = r.ReadSingle();

    int occCount = r.ReadInt32();
    _occupations.Clear();
    for (int i = 0; i < occCount; i++)
    {
        string nodeId     = r.ReadString();
        string attackerId = r.ReadString();
        float  progress   = r.ReadSingle();
        _occupations[nodeId] = (attackerId, progress);
    }
}
```

> **Note**: The system blob itself uses a simple `BinaryWriter` layout — not FlatBuffers.
> The blob is opaque to the outer snapshot format. System authors may use whatever binary
> format they prefer as long as it is deterministic and versioned.

---

## 9. Configuration

```csharp
// Assets/Scripts/Odengine/Serialization/SnapshotConfig.cs
namespace Odengine.Serialization
{
    public sealed class SnapshotConfig
    {
        /// <summary>
        /// Initial buffer size in bytes for the FlatBuffer builder.
        /// Tuned for ~50K field entries with a 200-string pool.
        /// Increase for large worlds to avoid internal realloc.
        /// </summary>
        public int InitialBufferBytes = 1 << 20; // 1MB

        /// <summary>
        /// After this many consecutive Delta snapshots, SnapshotWriter automatically
        /// emits a Full to bound replay seek cost. 0 = never auto-Full.
        /// </summary>
        public int DeltaChainMaxLength = 100;

        /// <summary>
        /// When true, validate that all field entry nodeIds exist in the graph before
        /// writing. Catches logic errors at the cost of a Dict lookup per entry.
        /// Recommended: true in debug/test builds, false in production.
        /// </summary>
        public bool ValidateGraphReferencesOnWrite = false;

        /// <summary>
        /// When true, check for NaN and Infinity in logAmp values during write.
        /// Should never trigger if ScalarField clamp is working correctly.
        /// Recommended: true in debug builds.
        /// </summary>
        public bool ValidateLogAmpSanityOnWrite = false;

        /// <summary>
        /// Always validate header magic and schema version on load.
        /// Cannot be disabled — corrupt snapshots are refused unconditionally.
        /// </summary>
        public bool ValidateGraphReferencesOnRead = true;

        /// <summary>
        /// Semver string baked into every snapshot header. Set at startup.
        /// </summary>
        public string EngineVersion = "0.1.0";

        /// <summary>
        /// When true, include graph topology (nodes + edges) even in Delta snapshots.
        /// Default false — graph is assumed stable between Full and Deltas.
        /// Enable when the graph can mutate during a run.
        /// </summary>
        public bool AlwaysIncludeGraph = false;
    }
}
```

---

## 10. Integration in the current codebase

### Folder layout

```
Assets/Scripts/Odengine/Serialization/
├── ISnapshotParticipant.cs        ← interface (§8)
├── SnapshotConfig.cs              ← config data class (§9)
├── SnapshotWriter.cs              ← write path (Full, Delta, Checkpoint)
├── SnapshotReader.cs              ← read path (ReadFull, ReadAtTick, ReadCheckpoint)
├── SnapshotData.cs                ← loaded result handle
├── DeltaIndex.cs                  ← index structure for a snapshot series on disk
├── schema/
│   └── odengine_snapshot.fbs      ← schema source (§3)
└── FlatBuffers/                   ← generated C# from flatc (do not edit by hand)
    ├── Snapshot.cs
    ├── SnapshotHeader.cs
    ├── FieldSnapshot.cs
    ├── ...
```

The `FlatBuffers/` directory is committed; it is regenerated by the build step when the `.fbs` changes (see §15).

### SnapshotWriter public API

```csharp
public sealed class SnapshotWriter
{
    public SnapshotWriter(SnapshotConfig config = null);

    /// <summary>Full snapshot. Self-contained. No parent.</summary>
    public byte[] WriteFull(
        Dimension dimension,
        ulong tick,
        double simTime,
        IReadOnlyList<ISnapshotParticipant> participants = null);

    /// <summary>Delta snapshot. Only records changes since previousFull or previousDelta.</summary>
    public byte[] WriteDelta(
        Dimension currentDimension,
        Dimension previousDimension,
        ulong tick,
        double simTime,
        ulong parentTick,
        ushort chainDepth);

    /// <summary>Checkpoint snapshot. Full state + system blobs. Required for resume.</summary>
    public byte[] WriteCheckpoint(
        Dimension dimension,
        ulong tick,
        double simTime,
        IReadOnlyList<ISnapshotParticipant> participants);
}
```

### SnapshotReader public API

```csharp
public sealed class SnapshotReader
{
    public SnapshotReader(SnapshotConfig config = null);

    /// <summary>Read a single Full or Checkpoint snapshot.</summary>
    public SnapshotData Read(byte[] bytes);

    /// <summary>Read a snapshot series (Full + Deltas) and reconstruct state at a given tick.</summary>
    public SnapshotData ReadAtTick(IReadOnlyList<byte[]> snapshotSeries, ulong tick);
}
```

### SnapshotData public API

```csharp
public sealed class SnapshotData
{
    public SnapshotHeader Header { get; }         // tick, simTime, type, etc.

    /// <summary>Reconstruct a standalone Dimension for analysis. No systems constructed.</summary>
    public Dimension ReconstructDimension();

    /// <summary>Restore graph topology into an existing, empty Dimension.</summary>
    public void RestoreGraph(Dimension dim);

    /// <summary>Restore all field registrations and sparse entry data into dim.</summary>
    public void RestoreFields(Dimension dim);

    /// <summary>
    /// Restore a participant's state from the matching SystemBlob.
    /// Calls DeserializeSystemState then PostLoad.
    /// Throws if snapshot type is not Checkpoint.
    /// Throws if no blob matches participant.SystemId (use TryRestoreSystem for optional).
    /// </summary>
    public void RestoreSystem(ISnapshotParticipant participant);
    public bool TryRestoreSystem(ISnapshotParticipant participant);
}
```

### DeltaIndex — managing series on disk

For run recording (continuous per-tick deltas), the game layer creates a `DeltaIndex` that tracks which byte offsets in a stream (or which files in a folder) correspond to which ticks. This allows O(log N) seek to any tick without loading all snapshots.

```csharp
public sealed class DeltaIndex
{
    public void Append(ulong tick, SnapshotType type, long byteOffset, int byteLength);
    public (long offset, int length)? FindFullBefore(ulong tick);
    public IReadOnlyList<(long offset, int length)> FindDeltaRange(ulong fullTick, ulong targetTick);
    public void SaveIndex(Stream stream);
    public static DeltaIndex LoadIndex(Stream stream);
}
```

---

## 11. Design contract going forwards

### What every new system author must do

When adding a new domain system to Odengine:

**Step 1 — Identify state categories**

For each piece of state the system holds, classify it:

| Category                               | Example                       | Action                                  |
| -------------------------------------- | ----------------------------- | --------------------------------------- |
| Stored in `ScalarField`                | `EconomySystem.Availability`  | Nothing — serialized automatically      |
| Non-field, not reconstructable         | `WarSystem._activeWarNodes`   | **Implement `ISnapshotParticipant`**    |
| Non-field, reconstructable from fields | `FactionSystem._lastDominant` | Implement `PostLoad()` only             |
| Purely transient / event plumbing      | Callback delegates            | Don't serialize; re-register after load |

**Step 2 — If implementing ISnapshotParticipant**

1. Choose a `SystemId` string following the convention `"domain.systemname"` (e.g. `"combat.system"`). This string must never change — it is the primary key for the blob in saved files.
2. Start `SerializeSystemState()` with a version byte. Start at `(byte)1`.
3. Sort all collections before writing — blobs must be deterministic.
4. Write a `DeserializeSystemState` that handles version 1 and knows how to migrate from older versions by switching on the version byte.
5. Write unit tests for round-trip fidelity (see §14 for the test pattern).

**Step 3 — Register with SnapshotWriter**

The game layer (not Odengine) holds the list of participants. Odengine does not auto-discover them. The game layer passes the list explicitly to `WriteCheckpoint`. This keeps Odengine agnostic to which systems are present in a given game.

**Step 4 — Document what is and is not captured**

Add a comment block in the system class noting:

```csharp
// ── Serialization ──────────────────────────────────────────────────────
// Field state (ScalarField): serialized automatically by SnapshotWriter.
// Extra non-field state: serialized via ISnapshotParticipant (SystemId = "war.system").
// Reconstructable caches: none (all derived on observation from fields).
// Callbacks/delegates: not serialized; game layer must re-register after load.
```

### What must NOT live in non-participant state

Any data that must survive save/load must be in a `ScalarField` or in a participant blob. If you find yourself with a `Dictionary` of domain facts outside both of these, you have a design problem. Either:

- Move it into a `ScalarField` channel (preferred — it becomes propagatable and observable), or
- Add the system to the participant list.

---

## 12. Game-layer usage patterns

Odengine is game-agnostic. The game layer is responsible for:

- Deciding _when_ to save (player-triggered save, autosave cadence, end-of-run).
- Managing file paths and file formats around the snapshot bytes.
- Storing its own additional save data (player inventory, quest flags, etc.) alongside the Odengine snapshot.
- Re-registering callbacks after load-and-resume.

### Pattern A: postmortem analysis run recording

```csharp
// At simulation start:
var writer = new SnapshotWriter(config);
var snapshots = new List<byte[]>();
var index = new DeltaIndex();
Dimension previousDim = null;
int chainDepth = 0;

// Each tick (or every N ticks):
void RecordTick(Dimension dim, ulong tick, double time)
{
    byte[] snap;
    if (previousDim == null || chainDepth >= config.DeltaChainMaxLength)
    {
        snap = writer.WriteFull(dim, tick, time);
        chainDepth = 0;
    }
    else
    {
        snap = writer.WriteDelta(dim, previousDim, tick, time,
            parentTick: tick - 1, chainDepth: (ushort)chainDepth);
        chainDepth++;
    }

    index.Append(tick, header.SnapshotType, stream.Position, snap.Length);
    stream.Write(snap, 0, snap.Length);
    previousDim = dim.DeepCopy(); // or keep a ref to last tick's dimension if immutable
}

// At run end, save the index:
index.SaveIndex(indexStream);
```

> **Note**: `Dimension.DeepCopy()` is not yet implemented. For delta computation,
> the writer needs either a previous Dimension reference or a materialized snapshot of
> the previous tick's field state. The simplest implementation: keep the previous tick's
> Full snapshot bytes in memory and let the DeltaWriter deserialize them only to compute
> the diff. This avoids requiring Dimension to be immutable or cloneable.

### Pattern B: game save slot

```csharp
// Player presses Save:
void SaveGame(string slotPath, Dimension dim, ulong tick, double time,
              WarSystem war, FactionSystem factions, EconomySystem economy)
{
    // Odengine snapshot (Checkpoint includes system blobs)
    byte[] simState = writer.WriteCheckpoint(dim, tick, time,
        participants: new ISnapshotParticipant[] { war });
    // (FactionSystem and EconomySystem are not participants — field-only state)

    // Game's own data alongside
    var gameSave = new GameSaveData { /* player stats, quests, etc. */ };
    byte[] gameBytes = gameSave.Serialize();

    // Write a simple container: 4-byte length prefix + simState + gameBytes
    using var fs = File.Create(slotPath);
    fs.WriteInt32(simState.Length);
    fs.Write(simState);
    fs.Write(gameBytes);
}
```

### Pattern C: load and resume

```csharp
void LoadGame(string slotPath)
{
    using var fs = File.OpenRead(slotPath);
    int simLen = fs.ReadInt32();
    var simBytes = fs.ReadBytes(simLen);
    var gameBytes = fs.ReadToEnd();

    // Restore Odengine state
    var reader = new SnapshotReader(config);
    SnapshotData snap = reader.Read(simBytes);

    var dim = new Dimension();
    snap.RestoreGraph(dim);
    snap.RestoreFields(dim);

    var exposureProfile = new FieldProfile("war.exposure") { DecayRate = 0f };
    var war = new WarSystem(dim, exposureProfile);
    snap.RestoreSystem(war); // reads blob, calls PostLoad

    var factions = new FactionSystem(dim,
        MakePresenceProfile(), MakeInfluenceProfile(), MakeStabilityProfile());
    factions.PostLoad(); // rebuilds _lastDominant from Presence field

    var economy = new EconomySystem(dim, MakeEconomyProfile());
    // No blob needed for economy

    // Re-register game callbacks
    war.OnOccupationComplete = (nodeId, attackerId) => { /* ... */ };
    factions.OnDominanceChanged = (nodeId, prev, next) => { /* ... */ };

    // Restore game layer data
    GameSaveData.Deserialize(gameBytes);

    // Ready to tick
    sim.Resume(dim, war, factions, economy, tick: snap.Header.tick);
}
```

### Ensuring it "always works" — the game layer contract

Odengine cannot guarantee correctness of game-layer save/load because it does not own the game's full state. The game layer must follow these rules:

1. **Profiles must be deterministic.** `FieldProfile` instances passed to system constructors must be constructed identically on load as on save. If a profile's `DecayRate` differs, the simulation will diverge. The best approach: construct profiles from a central factory/config that loads from a stable config asset.

2. **System construction order must be stable.** Fields are registered in `Dimension` in construction order. Loading respects registration order for field lookup (by `fieldId` string, so order is actually irrelevant for correctness — but document it anyway).

3. **Callbacks are never serialized.** After load-and-resume, the game layer must re-register all `OnOccupationComplete`, `OnDominanceChanged`, etc. before the first `Tick`. Failing to do so means the first tick fires no callbacks.

4. **The graph must match on resume.** If the game mutates the graph between a Checkpoint and loading it, the loaded field entries may reference nodes that no longer exist. `SnapshotReader` with `ValidateGraphReferencesOnRead = true` will catch this.

5. **Test the save/load path in CI**, not just in manual play. See §14 for the test pattern.

---

## 13. Edge cases

| Case                                                        | Detection                                                                                                                  | Handling                                                                                     |
| ----------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| File does not start with `ODSN` identifier                  | `SnapshotReader` checks `file_identifier`                                                                                  | Throw `InvalidSnapshotException` with message                                                |
| `schema_version` in header is newer than current code knows | Version check in reader                                                                                                    | Throw `SnapshotVersionException`; do not partially load                                      |
| `schema_version` is older — forward compat                  | FlatBuffers tables are read with defaults for unknown fields                                                               | Transparent — no action needed                                                               |
| `SystemBlob.schema_version` is newer than system's code     | `DeserializeSystemState` receives unknown version                                                                          | System throws `NotSupportedException` with version info                                      |
| `SystemBlob.schema_version` is older — migration needed     | Version byte in blob payload                                                                                               | System migrates inside `DeserializeSystemState` via version switch                           |
| Field in snapshot not known to current Dimension            | `RestoreFields` creates a warning log and skips                                                                            | Field is silently absent post-load; no exception                                             |
| Node referenced in field entry not in restored graph        | `ValidateGraphReferencesOnRead` catches this                                                                               | Warning log + skip entry (field data is loaded but will not propagate)                       |
| NaN or Infinity in `log_amp`                                | `ValidateLogAmpSanityOnWrite = true` catches at write time; reader always validates if `ValidateLogAmpSanityOnRead = true` | Throw on write; clamp to `MinLogAmpClamp` on read with warning                               |
| Delta references a `parent_tick` not present in the index   | `ReadAtTick` walks the series backwards to find the Full                                                                   | If no Full found, throw `MissingParentSnapshotException`                                     |
| Delta chain deeper than `DeltaChainMaxLength` on read       | Reader reconstructs anyway — depth is advisory for the writer                                                              | No error; deep chains are slower to reconstruct                                              |
| Empty field (zero active entries)                           | Field snapshot written with `entries: []`                                                                                  | `FieldProfile` is preserved; field exists in Dimension but has no active entries — correct   |
| String pool has >65535 entries                              | Pool uses `uint32` indices — supports ~4 billion unique strings                                                            | No practical limit                                                                           |
| Load-and-resume called on a Full (not Checkpoint) snapshot  | `RestoreSystem` checks `SnapshotType`                                                                                      | Throw `InvalidOperationException`: "Cannot resume from a Full snapshot — use a Checkpoint"   |
| Graph topology changed between save and resume              | `ValidateGraphReferencesOnRead`                                                                                            | Field entries for removed nodes are skipped with a warning; new nodes start neutral          |
| Two systems share a `SystemId`                              | `SnapshotWriter` checks for uniqueness on write                                                                            | Throw `DuplicateSystemIdException` during write                                              |
| Dimension has a field that the snapshot does not mention    | Field is constructed by the system constructor but has zero entries                                                        | Field remains at neutral — correct; corresponds to a new field added after the save was made |

---

## 14. Test strategy

All tests live in `Assets/Scripts/Tests/Tests/Snapshots/` and run under Unity EditMode.

### Tier 1 — Unit: round-trip fidelity

```csharp
[Test]
public void Snapshot_RoundTrip_FullSnapshot_IsExact()
{
    // Build a Dimension with known field state
    var dim = BuildTestDimension(nodes: 10, fields: 3, entriesPerField: 50);
    var writer = new SnapshotWriter(new SnapshotConfig());
    byte[] bytes = writer.WriteFull(dim, tick: 42, simTime: 3.14);

    var reader = new SnapshotReader();
    SnapshotData snap = reader.Read(bytes);
    Dimension restored = snap.ReconstructDimension();

    // Every active entry in original must appear in restored
    AssertDimensionsEqual(dim, restored);
    Assert.That(snap.Header.tick, Is.EqualTo(42UL));
}
```

### Tier 2 — Unit: sparsity

```csharp
[Test]
public void Snapshot_DoesNotWrite_NeutralEntries()
{
    var dim = new Dimension();
    dim.AddNode("a");
    var f = dim.AddField("f", new FieldProfile("fp"));
    // Set logAmp below threshold — SetLogAmp removes the entry
    f.SetLogAmp("a", "ch", 0.00005f);  // below LogEpsilon → pruned
    Assert.That(f.GetLogAmp("a", "ch"), Is.EqualTo(0f));

    byte[] bytes = new SnapshotWriter().WriteFull(dim, 0, 0);
    var snap = new SnapshotReader().Read(bytes);
    var restored = snap.ReconstructDimension();
    Assert.That(restored.GetField("f").GetLogAmp("a", "ch"), Is.EqualTo(0f));
}
```

### Tier 3 — Unit: delta round-trip

```csharp
[Test]
public void Snapshot_Delta_ReconstructsExactStateAtTargetTick()
{
    var dim = BuildTestDimension(nodes: 5, fields: 2, entriesPerField: 20);
    var writer = new SnapshotWriter();
    var series = new List<byte[]>();

    // Tick 0: Full
    series.Add(writer.WriteFull(dim, tick: 0, simTime: 0));

    // Tick 1: mutate + Delta
    dim.GetField("f1").AddLogAmp("n1", "c1", 0.5f);
    series.Add(writer.WriteDelta(dim, /* previous */ series[0], tick: 1, simTime: 1, parentTick: 0, chainDepth: 1));

    // Reconstruct at tick 1
    var snap = new SnapshotReader().ReadAtTick(series, targetTick: 1);
    var restored = snap.ReconstructDimension();
    Assert.That(restored.GetField("f1").GetLogAmp("n1", "c1"),
        Is.EqualTo(0.5f).Within(1e-6f));
}
```

### Tier 4 — Unit: system blob round-trip

```csharp
[Test]
public void Snapshot_WarSystem_BlobRoundTrip_ResumesCorrectly()
{
    var dim = new Dimension();
    dim.AddNode("earth"); dim.AddNode("mars");
    var war = new WarSystem(dim, MakeExposureProfile());
    war.DeclareWar("earth");
    war.DeclareOccupation("mars", "empire_red");
    war.SetNodeStability("mars", 0.7f);
    war.Tick(5f);

    byte[] checkpoint = new SnapshotWriter().WriteCheckpoint(
        dim, tick: 5, simTime: 5.0,
        participants: new[] { war });

    var dim2 = new Dimension();
    var snap = new SnapshotReader().Read(checkpoint);
    snap.RestoreGraph(dim2);
    snap.RestoreFields(dim2);

    var war2 = new WarSystem(dim2, MakeExposureProfile());
    snap.RestoreSystem(war2);

    Assert.That(war2.IsAtWar("earth"), Is.True);
    Assert.That(war2.GetOccupationAttacker("mars"), Is.EqualTo("empire_red"));
    Assert.That(war2.GetNodeStability("mars"), Is.EqualTo(0.7f).Within(1e-6f));

    // After restore, simulation can continue: tick 6 should not throw
    Assert.DoesNotThrow(() => war2.Tick(1f));
}
```

### Tier 5 — Determinism: identical state → identical bytes

```csharp
[Test]
public void Snapshot_SameState_ProducesIdenticalBytes()
{
    byte[] a = WriteKnownState();
    byte[] b = WriteKnownState();
    Assert.That(a, Is.EqualTo(b),
        "Snapshot byte output must be deterministic across invocations");
}
```

### Tier 6 — Performance: large world benchmarks

These tests assert on timing, not just correctness. Run them in EditMode with `[Timeout(10000)]`.

```csharp
[Test, Timeout(10000)]
public void Performance_LargeWorld_FullWrite_Under16ms()
{
    // ~1M active field entries: 1000 nodes × 5 fields × 200 channels
    var dim = GenerateLargeWorld(nodeCount: 1000, fieldCount: 5,
        channelCount: 200, density: 1.0f);

    var writer = new SnapshotWriter();
    var sw = Stopwatch.StartNew();
    byte[] bytes = writer.WriteFull(dim, 0, 0);
    sw.Stop();

    Assert.That(sw.ElapsedMilliseconds, Is.LessThan(16),
        $"Full write took {sw.ElapsedMilliseconds}ms — exceeds 16ms budget");
    Assert.That(bytes.Length, Is.GreaterThan(0));
    UnityEngine.Debug.Log($"[Perf] 1M entries → {bytes.Length / 1024}KB in {sw.ElapsedMilliseconds}ms");
}

[Test, Timeout(10000)]
public void Performance_LargeWorld_FullRead_Under16ms()
{
    var dim = GenerateLargeWorld(1000, 5, 200, 1.0f);
    byte[] bytes = new SnapshotWriter().WriteFull(dim, 0, 0);

    var sw = Stopwatch.StartNew();
    var snap = new SnapshotReader().Read(bytes);
    Dimension restored = snap.ReconstructDimension();
    sw.Stop();

    Assert.That(sw.ElapsedMilliseconds, Is.LessThan(16));
}

[Test, Timeout(10000)]
public void Performance_Delta_Under2ms_ForSmallChanges()
{
    // Large world, but only 0.1% entries changed per tick
    var dim = GenerateLargeWorld(1000, 5, 200, 1.0f);
    byte[] fullSnap = new SnapshotWriter().WriteFull(dim, 0, 0);

    // Mutate 1000 entries (0.1% of 1M)
    MutateEntries(dim, count: 1000);

    var writer = new SnapshotWriter();
    var sw = Stopwatch.StartNew();
    byte[] delta = writer.WriteDelta(dim, fullSnap, tick: 1, simTime: 1, parentTick: 0, chainDepth: 1);
    sw.Stop();

    Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2));
    UnityEngine.Debug.Log($"[Perf] 1000-entry delta → {delta.Length / 1024}KB in {sw.ElapsedMilliseconds}ms");
}

/// <summary>
/// Generates a Dimension with the requested number of active entries at approximately
/// the given density. Uses DeterministicRng for reproducibility.
/// </summary>
private static Dimension GenerateLargeWorld(int nodeCount, int fieldCount,
    int channelCount, float density)
{
    var rng = new DeterministicRng(0xDEADBEEF);
    var dim = new Dimension();

    for (int n = 0; n < nodeCount; n++) dim.AddNode($"node_{n:D4}");

    for (int f = 0; f < fieldCount; f++)
    {
        var field = dim.AddField($"field_{f}", new FieldProfile($"prof_{f}"));
        for (int n = 0; n < nodeCount; n++)
            for (int c = 0; c < channelCount; c++)
                if (rng.NextFloat() < density)
                    field.SetLogAmp($"node_{n:D4}", $"ch_{c:D3}",
                        (rng.NextFloat() * 2f - 1f) * 10f); // range [-10, 10]
    }

    return dim;
}
```

### Tier 7 — Schema evolution tests

```csharp
[Test]
public void Schema_ForwardCompat_UnknownFieldsIgnored()
{
    // Load a snapshot "from the future" that has extra fields we don't know about.
    // FlatBuffers tables are forward-compatible — extra fields are silently skipped.
    // This test validates that no exception is thrown.
    byte[] futureSnap = LoadEmbeddedTestAsset("future_schema_v2.odsn");
    Assert.DoesNotThrow(() => new SnapshotReader().Read(futureSnap));
}

[Test]
public void Schema_WrongMagic_ThrowsInvalidSnapshot()
{
    byte[] garbage = new byte[128];
    new Random(42).NextBytes(garbage);
    Assert.Throws<InvalidSnapshotException>(() => new SnapshotReader().Read(garbage));
}
```

---

## 15. Tooling and build integration

### FlatBuffers compiler

`flatc` (the FlatBuffers schema compiler) is **not** run at Unity play time. It is run as a pre-build step that regenerates the C# files in `Assets/Scripts/Odengine/Serialization/FlatBuffers/` whenever the `.fbs` schema changes.

**Recommended setup:**

```bash
# In the project root, add a Makefile target or a pre-commit hook:
flatc --csharp --gen-onefile \
  --no-warnings \
  -o Assets/Scripts/Odengine/Serialization/FlatBuffers/ \
  Assets/Scripts/Odengine/Serialization/schema/odengine_snapshot.fbs
```

Check the generated files into git. This keeps the build hermetic — developers do not need `flatc` installed unless they modify the schema.

**Updating the schema:**

1. Edit `odengine_snapshot.fbs` following the evolution rules in §3.
2. Run `flatc` as above.
3. Review the generated diff to confirm only expected changes.
4. Update `CurrentSchemaVersion` constant in `SnapshotReader` and `SnapshotWriter`.
5. Add a migration entry in `SnapshotReader` if schema semantics changed.
6. Run the full schema evolution test suite before committing.

### FlatBuffers C# runtime dependency

The FlatBuffers C# runtime is a **single source file**: `FlatBuffers/FlatBuffers.cs`.  
Add it to `Assets/Scripts/Odengine/Serialization/FlatBuffers/FlatBuffers.cs`.  
No NuGet, no `Packages/manifest.json` changes, no DLLs. This is intentional — Unity's package system and FlatBuffers do not play well together; vendoring the single file is simpler and more portable.

Current tested version: FlatBuffers 24.3.25 (C# runtime, `unsafe` mode enabled for zero-copy reads).

### Unsafe mode note

The zero-copy read path requires `unsafe` code in C#. Enable it for the `Odengine.Tests` assembly definition and the runtime assembly definition if you use the memory-mapped read path. For most snapshot sizes the safe managed path is fast enough and does not require `unsafe`.

---

## Summary checklist for maintainers

When adding a **new domain system**:

- [ ] Classify all private state (field-backed / non-field / reconstructable)
- [ ] Implement `ISnapshotParticipant` if non-field, non-reconstructable state exists
- [ ] If `ISnapshotParticipant`: choose a stable `SystemId`, start blob version at `1`, write sorted output
- [ ] If reconstructable caches: implement `PostLoad()`
- [ ] Add the system to the standard load-and-resume example in the game bootstrap
- [ ] Write Tier 4 (blob round-trip) test
- [ ] Add serialization notes comment block to the system class

When **modifying the schema** (`.fbs` file):

- [ ] Only append fields to existing tables — never reorder or remove
- [ ] Bump `schema_version` if semantics change
- [ ] Regenerate `FlatBuffers/` with `flatc`
- [ ] Run schema evolution tests
- [ ] Update this document's schema section

When **modifying ScalarField**:

- [ ] If `FieldProfile.LogEpsilon` default changes: existing profiles constructed without an explicit value will use the new default on load. If a snapshot stores the old value in `log_epsilon`, it will be restored correctly — no action needed. Document the change in release notes.
- [ ] If a system's tuning constants change: ensure they are in a `*Config` struct that is stored in the system blob (see §7 system-constants note). If they are still `private const`, changing them causes silent divergence on resume.
