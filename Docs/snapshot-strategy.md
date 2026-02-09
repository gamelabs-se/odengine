# Snapshot Strategy

## Goal

Deterministic time-travel debugging and replay for Odengine simulations.

## Approach

### Phase 1: Custom Binary Format (NOW)

**Why custom binary first:**
- Full control over layout
- Zero dependencies
- Optimized for our exact use case (sparse channelized fields)
- Simple to implement and understand

**Key features:**
- Deterministic serialization (stable ordering)
- Sparse amplitude storage (only non-zero values)
- Delta snapshots (only changed channels/nodes since last snapshot)
- String table deduplication (IDs stored once)
- Versioned format (future-proof)

**Format structure:**
```
Header: magic, version, tick, time
StringTable: deduplicated IDs
Graph: nodes, edges (sparse)
Fields: per-field → per-channel → sparse (nodeId, amp) pairs
Deltas: optional "changed since last" flag for incremental saves
```

**Performance characteristics:**
- Fast write (sequential, minimal allocations)
- Fast read (structured binary, no parsing)
- Compact (delta + sparse + string dedup)
- Large datasets: O(changed) not O(total)

### Phase 2: FlatBuffers (LATER, MAYBE)

**When to consider:**
- After we have 10+ fields, 1000+ items, 10k+ nodes
- When we need sub-millisecond random access during replay
- When we want schema evolution without versioning hell

**Why FlatBuffers is good (ChatGPT was right, partially):**
- Zero-copy deserialization (mmap the file, read directly)
- Very fast for large datasets with random access patterns
- Schema-driven (less manual versioning)

**Why custom binary is better for now:**
- Simpler (no schema compiler, no external deps)
- Easier to debug (just hex dump)
- Deltas are trivial to implement
- We don't need zero-copy yet (our snapshots are ~MB not ~GB)

**Trade-off:**
- FlatBuffers = faster random read, but harder to write deltas
- Custom binary = flexible, simple, fast enough for now

### Decision

Start with **custom binary + deltas**.

Migrate to FlatBuffers if/when:
- Snapshot sizes > 100MB regularly
- Replay needs sub-frame scrubbing (millisecond granularity)
- We add remote debugging over network

## Implementation Timeline

1. **Now:** Define `BinarySnapshot` format (header + string table + sparse fields)
2. **Soon:** Implement `SnapshotWriter.WriteFull()` and `SnapshotReader.ReadFull()`
3. **Later:** Add `SnapshotWriter.WriteDelta()` (incremental)
4. **Much later:** Evaluate FlatBuffers if performance demands it

## Notes

- Snapshots are append-only (time-series)
- One snapshot per tick (or configurable cadence)
- Determinism guaranteed by sorted IDs everywhere
- Compression (LZ4/Zstd) optional later, not now

---

**TL;DR:** Custom binary with delta support first. FlatBuffers only if we prove we need it.
