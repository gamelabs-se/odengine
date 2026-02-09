# Odengine V2 - Core Implementation Summary

## What We Built

A minimal, amplitude-based field simulation engine following the "deterministic quantum" architecture.

## Core Classes

### 1. **OdNode** (`Graph/OdNode.cs`)
- Represents any entity in the simulation (planet, system, ship, etc.)
- Has: `Id`, `ParentId`, `Tags`
- **No game logic** - just structure

### 2. **OdEdge** (`Graph/OdEdge.cs`)
- Connects two nodes
- Has: `From`, `To`, `Resistance`
- Resistance determines how fields attenuate across this edge

### 3. **OdNodeGraph** (`Graph/OdNodeGraph.cs`)
- Manages the collection of nodes and edges
- Provides: Add/Query nodes, ancestor chains, edge connections

### 4. **OdField** (`Fields/OdField.cs`)
- Represents a scalar field over the graph
- Stores **Amplitude (Amp)** at each node
- Examples: `econ.availability.water`, `war.power`, `politics.influence`
- **Fields don't store "stuff"** - only scalar values

### 5. **Amplitude** (`Core/Amplitude.cs`)
- Simple float wrapper representing field intensity at a node
- This is the **only** state that exists
- Everything else (prices, stock, units) is derived

### 6. **FieldProfile** (`Fields/FieldProfile.cs`)
- Defines how a field behaves:
  - Propagation rate
  - Resistance sensitivity
  - Decay/stabilization
- **Universal rules** - no domain-specific hacks

### 7. **FieldPropagator** (`Fields/FieldPropagator.cs`)
- Propagates amplitude across edges
- Uses: `TransmittedAmp = Amp × exp(-Resistance × ResistanceScale)`
- Same mechanism for ALL fields (economy, war, culture, etc.)

### 8. **FieldSampler** (`Fields/FieldSampler.cs`)
- Reads amplitude from fields
- **Observation doesn't mutate** (quantum-inspired)
- Deterministic given same world state

### 9. **OdWorld** (`Core/OdWorld.cs`)
- Container for nodes, edges, and fields
- Minimal API:
  - `AddNode()`, `GetNode()`
  - `AddEdge()`
  - `GetOrCreateField()`
- **No engines, no game logic** - just state

## Design Decisions & Rationale

### Why Amplitude?

**Problem:** Traditional sims store explicit objects (items, units, etc.), causing:
- Infinite storage problems
- Synchronization nightmares  
- "What happens when you're not looking?"

**Solution:** Store only scalar **amplitude** (density/intensity/presence).
- When you observe: amplitude → realized objects
- Close UI: objects disappear, amplitude remains
- Reopen: same amplitude → same result (deterministic)

### Why Fields Over Graph?

**Inspired by:** Physics (electromagnetic fields), not game conventions.

**Benefits:**
- Universal propagation rules
- Natural attenuation (resistance)
- No special cases per domain
- Emergent complexity from simple rules

### Why Single Resistance Property?

**Old way:** Edges have `navalCost`, `landCost`, `tradeCost`, etc. → explosion of properties.

**New way:** Edge has one `Resistance`. Each field interprets it:
```
Army field: R_eff = Resistance × 1.0  (blocked)
Trade field: R_eff = Resistance × 0.4  (passes)
Culture field: R_eff = Resistance × 0.1  (mostly free)
```

Same edge, different behaviors. Zero special-casing.

### Why "Quantum"?

Not real quantum mechanics, but inspired by:
- **Superposition:** State doesn't "exist" until measured
- **Determinism:** Measurement is computed, not random
- **No spooky action:** Fields propagate via explicit rules

**In practice:**
- Prices don't exist until you open the market UI
- Army positions are amplitude until battle starts  
- Political influence is field until election happens

## What's Missing (Intentionally)

- **No engines yet** - just the graph and fields
- **No events** - that's the dynamic layer
- **No components** - will add when needed
- **No Luna integration** - this is pure Odengine

## Next Steps

1. **Add basic engine:** `FieldTickEngine` to propagate fields
2. **Add examples:** Show economy, war, politics as field configs
3. **Add tests:** Determinism, propagation, attenuation
4. **Add observables:** Price, stock, power - derived from amplitude
5. **Document patterns:** How to model new domains as fields

## Key Mental Models

**Think of it like:**
- **The Force** (Star Wars) - everywhere, shaped by terrain
- **Ocean currents** - flow, eddies, resistance
- **Heat diffusion** - spreads, attenuates, stabilizes
- **Electromagnetic fields** - universal propagation rules

**Not like:**
- Spreadsheets with cells
- Databases with tables
- Object graphs with pointers

## Philosophy

> "Everything is amplitude over a graph. Reality emerges when observed."

This is **not** a game engine. It's a **simulation substrate** that games can build on top of.

---

**Status:** Core architecture complete. Ready for first examples and tests.
