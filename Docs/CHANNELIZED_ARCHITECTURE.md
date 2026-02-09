# Odengine V2: Channelized Field Architecture

## What Changed

### 1. Channelized Fields (Scalability Fix)

**Problem**: Creating one `OdField` per item × per field type = combinatorial explosion
- 1000 items × 3 fields (availability, demand, supply) = 3000 field objects
- Memory/time balloon
- Most items are "cold" (never sampled) but still propagated

**Solution**: Single `OdScalarField` with channels (item IDs)
- One "Availability" field with 1000 channels ("water", "rifle", etc.)
- Channels are lazy-created on first use
- Only active channels propagate

**Mental Model Preserved**: `OdVirtualField` struct gives you "each item is a field" API
```csharp
// Conceptually separate fields, physically one field with channels
var waterAvail = availability.For("water");
var rifleAvail = availability.For("rifle");
waterAvail.SetAmp("planet_x", 100f);
```

### 2. Two-Phase Propagation (Determinism Fix)

**Problem**: Agent's code mutated amplitudes while iterating neighbors
- Order-dependent results
- Same-tick chain reactions (amplitude "copies" exponentially)
- Non-deterministic across platforms

**Solution**: Snapshot → Compute Deltas → Apply
```csharp
Phase 1: Read all source amplitudes (frozen snapshot)
Phase 2: Compute deltas into buffer (don't touch storage)
Phase 3: Apply deltas deterministically (sorted keys)
```

### 3. Edge Tags + Per-Field Tag Resistance

**Problem**: "Ocean blocks armies but not the Force" required special-casing

**Solution**: Edges have sorted string tags ("ocean", "road", "wormhole")
- `FieldProfile` has `TagMultipliers`: `{" ocean": 10.0, "road": 0.5}`
- Effective resistance = `edge.Resistance × profile.EdgeResistanceScale × tagMultiplier`
- No special cases, just math

**Example**:
```csharp
// Ocean edge
edge.AddTag("ocean");

// Army field: high ocean resistance
armyProfile.SetTagMultiplier("ocean", 10f);  // blocked

// Force field: ignores terrain
forceProfile.SetTagMultiplier("ocean", 1f);  // unaffected
```

### 4. Per-Channel Profile Overrides

**Problem**: "Rare artifacts propagate slower than water" without creating separate fields

**Solution**: `IChannelProfileProvider` + `ChannelProfileOverride`
```csharp
// Economy system provides per-item overrides
economySystem.SetItemOverride("rare_artifact", new ChannelProfileOverride {
    PropagationRate = 0.1f,
    EdgeResistanceScale = 2.0f
});
```

### 5. Conservation Modes (Radiation vs Diffusion)

**Problem**: Propagation was "copying" amplitude (non-conservative)
- Source amplitude stays full while transmitting to neighbors
- Accidental exponential growth unless decay dominates

**Solution**: `ConservationMode` enum in `FieldProfile`
- **Radiation**: Source unchanged, amplitude "broadcasts" (requires explicit decay)
- **Diffusion**: Transmitted amplitude subtracted from source (mass-conserving)

Choose per field based on desired physics.

---

## Core Classes

### `ChannelFieldStorage`
- Stores `amp[channelId][nodeId] = float`
- Tracks active channels (non-zero amplitude)
- Deterministic iteration (sorted)

### `OdScalarField`
- Single field with many channels
- Has one `FieldProfile`
- Optional `IChannelProfileProvider` for per-channel overrides
- Exposes `For(channelId)` → `OdVirtualField`

### `OdVirtualField`
- Lightweight struct: `(OdScalarField, channelId)`
- Keeps "each item is a field" mental model
- Just a view, no allocations

### `FieldPropagator`
- Static class with `Step(field, graph, dt, channels?)`
- Two-phase: snapshot → deltas → apply
- Tag-aware resistance computation

### `FieldProfile`
- Physics knobs: propagation rate, resistance scale, decay, minAmp
- Conservation mode: Radiation | Diffusion
- Tag multipliers: `{"ocean": 10f, "road": 0.5f}`

### `ChannelProfileOverride`
- Per-channel (item) overrides for all profile parameters
- Allows item-specific physics without field explosion

---

## Snapshot System (FlatBuffers)

Schema: `snapshot.fbs`

**Why FlatBuffers?**
- Zero-copy deserialization (access data without full parse)
- Best for large sparse datasets
- Deterministic binary format
- Versioning built-in

**Structure**:
```
WorldSnapshot {
  tick, world_time, version
  StringTable (dedupe all IDs)
  Nodes[] (id_index, name_index, parent_index)
  Edges[] (from_index, to_index, resistance, tag_indices[])
  ScalarFields[] {
    field_id_index, profile_id_index
    Channels[] {
      channel_id_index
      AmplitudeEntries[] (node_index, amplitude)  // sparse
    }
  }
}
```

**Sparse storage**: Only store non-zero amplitudes
**Deduped strings**: All IDs stored once in string table, referenced by index
**Sorted everywhere**: Nodes, edges, channels, amplitudes (determinism)

---

## What This Fixes

✅ **Determinism**: Two-phase propagation, sorted iteration
✅ **Scalability**: Channels instead of field explosion
✅ **Expressiveness**: Tags + multipliers (ocean, road, wormhole)
✅ **Physics correctness**: Conservation modes explicit
✅ **Item-specific behavior**: Channel overrides without separate fields
✅ **Snapshot/replay**: FlatBuffers binary format ready
✅ **Mental model**: "Each item is a field" preserved via VirtualField

---

## Migration from Old Code

**Old**:
```csharp
var waterField = new OdField("water.availability");
waterField.SetAmplitude("planet_x", 100f);
```

**New**:
```csharp
var availabilityField = new OdScalarField("availability", profile);
var waterAvail = availabilityField.For("water");
waterAvail.SetAmp("planet_x", 100f);
```

**Old propagation**:
```csharp
foreach (var node in nodes)
    FieldPropagator.PropagateFrom(node, field);  // BROKEN: order-dependent
```

**New propagation**:
```csharp
FieldPropagator.Step(availabilityField, graph, dt);  // deterministic two-phase
```

---

## Next Steps

1. ✅ Core architecture (done)
2. ✅ FlatBuffers schema (done)
3. ⏳ Generate C# from `snapshot.fbs` (run flatc compiler)
4. ⏳ Implement `SnapshotWriter` / `SnapshotReader`
5. ⏳ Update tests to use channelized fields
6. ⏳ Update economy example to use new architecture
7. ⏳ Integrate with Luna Odyssea

---

## Performance Notes

**Propagation cost**: O(active_channels × edges × dt)
- Only propagate channels with non-zero amp
- Optionally propagate only "region of interest" channels
- Deterministic channel selection = deterministic results

**Memory**: O(active_nodes × active_channels)
- Sparse storage: only non-zero amplitudes stored
- Channel maps use `Dictionary<string, float>` per channel

**Snapshot size**: ~linear in (nodes + edges + non-zero amplitudes)
- String table deduplicates all IDs
- Compression (LZ4/Zstd) can be added later if needed

---

## Design Philosophy Alignment

✅ **Deterministic**: Same inputs → same outputs, always
✅ **Quantum**: Amplitude doesn't "exist" until measured (sampling)
✅ **Minimal**: One concept (amplitude over graph), many interpretations
✅ **Extensible**: Tags, channels, overrides = configuration, not code
✅ **No legacy constraints**: Built from scratch, no compromises

**"The world is scalar fields flowing over a graph, shaped by nodes, attenuated by resistance, sampled into reality only when observed — with all complexity emerging from simple, universal rules."**
