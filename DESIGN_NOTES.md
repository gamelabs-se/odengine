# Odengine V2 - Design Notes

**Date**: 2026-02-06  
**Status**: Core foundation complete

## What We Built

A clean-slate implementation of Odengine following the quantum-field architecture described in `ODENGINE_DESIGN.md` from luna-odyssea.

### Core Classes

1. **OdNode** - Points/anchors in simulation (planets, ships, etc.)
2. **OdNodeGraph** - Topology layer (parent/child hierarchy)
3. **ScalarField** - Single field: (FieldId × NodeId) → float
4. **FieldStore** - Collection of all scalar fields
5. **MeasurementContext** - Observation context (seed, tick, observer, noise)
6. **Observable** - Base class for derived fields (computed on measurement)
7. **MeasurementCache** - Coherence guarantee (same tick = same result)
8. **OdWorld** - Complete simulation state
9. **Components** - EmitterComponent, SinkComponent (field modifiers)
10. **ComponentStore** - Storage for typed components
11. **Engines** - EmissionEngine, DiffusionEngine (state evolution)

### Key Design Decisions

#### 1. Fields Over Entities

**Why**: Traditional ECS stores discrete values. Fields store continuous distributions. This enables:
- Lazy evaluation (compute only what's observed)
- Natural propagation
- Observer-dependent measurements
- Emergent interactions

**Trade-off**: Slightly more complex mental model, but dramatically cleaner architecture.

#### 2. Observables (Derived Fields)

**Why**: Price/threat/stock are **projections**, not ground truth. Computing on-demand from stable fields is:
- Deterministic
- Cacheable
- Naturally consistent

**Implementation**: `Observable.Measure()` computes from other fields. Cache ensures coherence within tick.

#### 3. Measurement Cache

**Why**: Quantum-inspired: repeated measurement without change yields same result. Games: opening shop twice same frame should show same prices.

**Implementation**: Dictionary keyed by (tick, observerId, nodeId, fieldId). Cleared every tick.

#### 4. Deterministic Noise (Shimmer)

**Why**: Want "quantum-like" micro-variation (1-3%) without losing determinism.

**Implementation**: Hash(seed, tick, nodeId, fieldId, observerId) → noise ∈ [-1, 1]. Same inputs = same noise.

#### 5. Minimal Generic Components

**Why**: Components should be **field modifiers**, not game data. "ShipHullComponent" = wrong. "EmitterComponent" = right.

**Examples**:
- EmitterComponent: rate → field
- SinkComponent: field → consumption
- CouplingComponent (TODO): field → field influence

#### 6. Engines as Pure Functions

**Why**: Engines are time-evolution operators. Read state, emit deltas. No side effects, no Unity dependencies.

**Pattern**:
```csharp
public abstract void Tick(OdWorld world, float delta);
```

### Determinism Guarantees

1. **Stable iteration**: All dictionaries use `StringComparer.Ordinal`
2. **Sorted tags**: Node tags sorted at construction
3. **No hidden randomness**: All "random" values are deterministic hashes
4. **Coherent cache**: Same query same tick = same result
5. **No global state**: Everything flows through OdWorld

### What's Different From V1

| V1 (luna-odyssea) | V2 (this) |
|---|---|
| Nodes + Components | **Nodes + Fields + Components** |
| Prices stored | Prices **computed** (Observable) |
| Complex economy engine | Simple emission/diffusion |
| Unity-coupled | **Pure C#** (Unity-agnostic) |
| Legacy compatibility | **Clean slate** |

## Next Steps

### Phase 2: Coupling & Propagation

- CouplingComponent (field → field influence)
- Advanced diffusion (edge-based, log-saturation)
- Policy engine (modifiers, tariffs, rules-as-data)

### Phase 3: Movement & Pathing

- OdEdgeGraph (connectivity layer separate from topology)
- PathCost as field sampling (friction, risk, hazard)
- Movement engine (intent → position delta)

### Phase 4: War & Combat

- War fields (intensity, occupation, front_pressure)
- Combat as field exchange (deterministic)
- Threshold events (control change, occupation flip)

### Phase 5: Materialization

- Materializers: field → discrete units (armies, inventories)
- Deterministic generation (seed + fields → roster)
- Optional persistence (collapsed observations)

### Phase 6: Snapshot & Telemetry

- WorldSnapshot (deep copy, serializable)
- Telemetry ring buffer
- Offline diff tool (snapshot A vs B)

## Integration Notes

When integrating with luna-odyssea:
1. Keep V2 in separate namespace (`Odengine.Core`)
2. Create adapter layer (`Odengine.Luna`)
3. Gradually migrate systems
4. Run tests in parallel (V1 and V2)
5. Delete V1 only when V2 is proven

## Testing Philosophy

- **Determinism first**: Same seed = same result
- **Coherence**: Cache consistency
- **Conservation**: Diffusion doesn't create/destroy mass
- **Correctness**: Engine logic matches spec

Every engine must have:
1. Determinism test
2. Edge case test (empty, negative values)
3. Integration test (with other engines)

## Performance Notes

- **Lazy by default**: Only compute what's observed
- **Cache-friendly**: Fields are contiguous float arrays (in principle)
- **No allocations in hot path**: Use struct contexts, cached collections
- **Parallel-ready**: Engines are pure functions (can parallelize later)

## Open Questions

1. **Materialization persistence**: Store collapsed observations or recompute?
   - **Current answer**: Recompute by default, store if explicitly anchored
   
2. **Field dimensionality**: Should we support 2D/3D spatial fields?
   - **Current answer**: No, use node hierarchy + tags for spatial queries
   
3. **Observer permissions**: Should some observables be "intel-gated"?
   - **Current answer**: Yes, but implement in game layer, not core

4. **Field compression**: How to serialize sparse fields efficiently?
   - **Current answer**: Defer until snapshot system

## References

- Primary design: `/luna-odyssea/Assets/Scripts/Odengine/ODENGINE_DESIGN.md`
- Legacy implementation: `/luna-odyssea/Assets/Scripts/Odengine/`
- Test philosophy: `/luna-odyssea/TESTING_GUIDE.md`

---

**Conclusion**: Core foundation is solid, deterministic, and testable. Ready for vertical slice POC.
