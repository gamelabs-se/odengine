# Odengine V2 vs Luna Odyssea Integration Status

## Summary

**Odengine** (new project): Clean V2 architecture with channelized fields, two-phase propagation, edge tags
**Luna Odyssea**: V1 integration with trade UI, but needs V2 migration

---

## What's in Odengine V2 (Clean)

✅ **Core Architecture**
- `OdNodeGraph`: Nodes + edges with resistance + tags
- `OdScalarField`: Single field with channels (item IDs)
- `OdVirtualField`: "Each item is a field" mental model preserved
- `FieldPropagator`: Two-phase deterministic propagation
- `FieldProfile`: Physics knobs + tag multipliers + conservation mode
- `ChannelProfileOverride`: Per-item propagation overrides

✅ **Determinism Guarantees**
- Sorted iteration (nodes, edges, channels, amplitudes)
- Two-phase propagation (snapshot → deltas → apply)
- No order-dependent bugs
- Reproducible across runs/platforms

✅ **Scalability**
- Channels instead of per-item fields (1000 items = 1 field, not 1000 fields)
- Lazy channel activation (only propagate non-zero amplitudes)
- Sparse storage (only store non-zero values)

✅ **Snapshot System**
- FlatBuffers schema (`snapshot.fbs`)
- Zero-copy deserialization
- String table deduplication
- Sparse amplitude storage
- Ready for codegen (need to run flatc compiler)

✅ **Tests**
- 5 core tests passing
- Determinism verified
- Propagation verified
- Tag resistance verified

---

## What's in Luna Odyssea (V1 Integration)

⚠️ **Old Architecture Issues**
- One `OdField` per item (will explode with more items)
- In-place propagation (order-dependent, non-deterministic)
- No edge tags
- No channel system
- No snapshot system

✅ **What Works**
- Basic world loading (factions, commodities, systems, planets)
- Trade UI (O and T keys)
- Debug inspector (shows node graph, fields)
- Player location tracking
- Intent submission (but not processed correctly)

❌ **What's Broken**
- Trade intents don't update prices (propagation issues)
- Prices computed on-demand but not reconciled over time
- No lazy evaluation (all prices computed always)
- Food/medicine drop continuously (tick rate issues)
- OnGUI windows have layout errors
- No scope management (Peek/Watch/Lock not integrated)

---

## Migration Path: Luna → Odengine V2

### Phase 1: Drop-in Replacement (Minimal Breaking Changes)

**Goal**: Replace Luna's Odengine V1 with V2 as a library

**Steps**:
1. Package Odengine V2 as Unity package or DLL
2. Update `OdengineHost` to use V2 API
3. Update `OdengineDataLoader` to create channelized fields
4. Update trade intents to use channel-based API
5. Update UI to read from channelized fields

**Changes in Luna**:
```csharp
// Old
var waterField = world.GetField("water.availability");
float price = waterField.GetAmplitude("planet_x");

// New
var availabilityField = world.GetField("availability");
var waterAvail = availabilityField.For("water");
float amp = waterAvail.GetAmp("planet_x");
float price = ComputePrice(amp, demand, supply);  // measurement operator
```

### Phase 2: Integrate Snapshot System

**Goal**: Serialize world state for save/load/replay

**Steps**:
1. Generate C# code from `snapshot.fbs`
2. Implement `SnapshotWriter` in Luna
3. Implement `SnapshotReader` in Luna
4. Add "Save Snapshot" button to debug UI
5. Add "Load Snapshot" for replay/debugging

### Phase 3: Integrate Scope System (Peek/Watch/Lock)

**Goal**: Lazy price evaluation based on player observation

**Steps**:
1. Port `ScopeRegistry` from Luna V1 to Odengine V2
2. Integrate with `OdScalarField` (only propagate watched channels)
3. Update trade UI to call `Watch(location)` on open
4. Update trade UI to call `Unlock(location)` on close
5. Prices reconcile from snapshot when reopening

---

## Immediate Action Items

### For Odengine V2 Project

1. ✅ Core architecture (done)
2. ✅ FlatBuffers schema (done)
3. ⏳ Run `flatc --csharp snapshot.fbs` to generate C# code
4. ⏳ Implement `SnapshotWriter` class
5. ⏳ Implement `SnapshotReader` class
6. ⏳ Add economy example using new architecture
7. ⏳ Add tests for snapshot roundtrip

### For Luna Odyssea Project

1. ⏳ Copy/link Odengine V2 as package or DLL
2. ⏳ Update `OdengineHost` to V2 API
3. ⏳ Update `OdengineDataLoader` to create channels
4. ⏳ Fix trade intent processing
5. ⏳ Fix tick rate / delta time issues
6. ⏳ Remove OnGUI windows (or fix layout errors)
7. ⏳ Integrate Overwatch EditorWindow (read-only observer)

---

## Key Differences: V1 vs V2

| Feature | V1 (Luna) | V2 (Odengine) |
|---------|-----------|---------------|
| **Fields per item** | 1 field per item | 1 field with channels |
| **Propagation** | In-place (broken) | Two-phase (deterministic) |
| **Edge traits** | None | Tags + multipliers |
| **Conservation** | Implicit radiation | Explicit mode enum |
| **Overrides** | None | Per-channel profiles |
| **Snapshots** | None | FlatBuffers schema |
| **Determinism** | Order-dependent | Sorted + two-phase |
| **Scalability** | O(items × fields) | O(active_channels) |

---

## Testing Strategy

### Odengine V2 Tests (Unit)
- Core field operations
- Two-phase propagation determinism
- Tag-based resistance
- Channel overrides
- Snapshot roundtrip
- Conservation modes

### Luna Integration Tests (Runtime)
- Load world data → channelized fields
- Submit trade intent → amplitude changes
- Propagate fields → prices update
- Sample prices → deterministic results
- Save/load snapshot → identical state

---

## Performance Expectations

### V1 (Current Luna)
- 1000 items × 3 fields = 3000 field objects
- All fields propagate every tick
- Order O(3000 × edges × nodes) per tick

### V2 (Odengine)
- 3 fields with 1000 channels each
- Only active channels propagate
- Order O(active_channels × edges) per tick
- Expected: 10-100x faster for typical cases

---

## Documentation Sync

**Odengine Project**:
- `Docs/APPENDIX_I.md` - Field theory
- `Docs/CHANNELIZED_ARCHITECTURE.md` - V2 architecture
- `Assets/Scripts/Odengine/Serialization/snapshot.fbs` - Binary format

**Luna Project**:
- `ODENGINE_DESIGN.md` - High-level vision (sync with Odengine docs)
- `LAZY_WORLD_ARCHITECTURE.md` - Scope system (needs V2 update)
- `OVERWATCH_GUIDE.md` - Telemetry system (needs V2 update)

---

## Next Decision Point

**Question**: Do we migrate Luna to V2 incrementally, or finish V2 completely first?

**Option A: Incremental**
- Pros: See results faster, test in real game
- Cons: Half-broken state during transition

**Option B: Finish V2 First**
- Pros: Clean slate, no legacy bugs during development
- Cons: Takes longer before Luna benefits

**Recommendation**: Option B - Finish V2 core + snapshot system, then migrate Luna in one clean PR. The V2 architecture is stable enough now.
