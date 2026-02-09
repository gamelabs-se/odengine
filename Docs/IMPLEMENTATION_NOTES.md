# Odengine V2 - Implementation Notes

## Core Architecture Implemented

### 1. Amplitude (Core/Amplitude.cs)
- Scalar value representing field intensity at a node
- NOT a budget or inventory
- Represents density, presence, pressure
- Simple float wrapper with implicit conversions

### 2. OdNode (Graph/OdNode.cs)
- Basic node in the world graph
- Has Id, Name, Tags, Components, Edges
- Nodes are terrain - they shape fields
- No hard-coded game concepts

### 3. OdEdge (Graph/OdEdge.cs)
- Connection between two nodes
- Single core property: **Resistance**
- Tags for optional metadata
- The ONLY way fields propagate

### 4. OdField (Fields/OdField.cs)
- Scalar field over the node graph
- Stores Amplitude per node
- Does NOT store objects/items/entities
- Clean dictionary-based storage

### 5. FieldProfile (Fields/FieldProfile.cs)
- Defines how a field behaves
- PropagationRate: how fast it spreads
- EdgeResistanceScale: how much it cares about resistance
- DecayRate: natural attenuation
- StabilizationThreshold: when to stop propagating

### 6. FieldPropagator (Fields/FieldPropagator.cs)
- Propagates amplitudes across edges
- Formula: `TransmittedAmp = Amp × exp(-R_eff)`
- Where: `R_eff = Edge.Resistance × Field.EdgeResistanceScale`
- This is where "echoes" emerge from

### 7. FieldSampler (Fields/FieldSampler.cs)
- Samples field at a node to get observable values
- Deterministic and read-only
- Game layer uses samplers to "realize" amplitudes
- Example: Availability field → market stock units

### 8. OdWorld (Core/OdWorld.cs)
- Container for nodes, edges, fields
- No game logic
- Just graph + fields
- Clean, minimal API

## Design Decisions

### Why Amplitude?
Unified concept replacing: value, strength, stock, power, influence, supply, pressure.
Single scalar that means different things in different contexts.

### Why NOT store objects?
- Infinite storage problems solved
- No bookkeeping explosion
- No "what happens when you're not looking" issues
- Clean determinism

### Why Resistance-based propagation?
- Universal mechanic for all fields
- Same edge behaves differently for different fields
- No special cases for oceans/mountains/etc
- Just: `exp(-R_eff)` everywhere

### Why FieldProfile per field?
- Fields are peers
- No privileged field types
- Economy, war, culture all use same primitives
- Extensibility without core changes

### Why FieldSampler pattern?
- Clean separation: simulation vs observation
- Samplers are deterministic pure functions
- Game layer controls "realization" logic
- Odengine never knows about "prices" or "units"

## What's NOT Implemented Yet

1. **Engines** - systems that modify amplitudes based on rules
2. **Tick system** - time progression
3. **Intents** - external requests to change state
4. **Events** - notifications of state changes
5. **Scope system** - lazy evaluation (Peek/Watch/Lock)
6. **Serialization** - save/load snapshots
7. **Reconciliation** - time-based evolution when unobserved

## Next Steps

1. Implement SimClock (deterministic time)
2. Implement Engine pattern (modify fields per tick)
3. Implement Intent queue (external input)
4. Implement Event log (observable output)
5. Build first domain: Economy with Availability/Price fields
6. Add scope system for lazy evaluation
7. Add snapshot/telemetry for debugging

## Philosophy Check

✅ Deterministic - Same inputs → same outputs  
✅ Minimal - Core is <500 lines  
✅ Abstract - No game concepts in core  
✅ Extensible - Add fields without changing core  
✅ Universal rules - Same mechanics everywhere  
✅ Quantum-inspired - Amplitudes sampled on demand  
✅ No legacy - Built from scratch  

## Example Usage

See `Examples/BasicFieldExample.cs` for a working demo of:
- Creating nodes
- Connecting with edges (varying resistance)
- Creating a field with a profile
- Setting initial amplitude
- Propagating over time
- Sampling to get concrete values
- Observing emergent "echo" behavior

