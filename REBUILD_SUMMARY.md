# Odengine Rebuild - Complete

## What Was Done

### Deleted (Commit A)
- All old "Od" prefixed classes
- Old Field abstract base, LayeredField, ChanneledField
- Old ChannelFieldStorage, VirtualField, merge/normalization logic
- Old in-place mutating FieldPropagator
- Old Economy with per-commodity fields
- All old tests

### Implemented (Commits B-E)

**Graph Layer (B)**
- `Node.cs` - Simple node with Id, Name
- `Edge.cs` - Edge with FromId, ToId, Resistance, Tags (HashSet<string>)
- `NodeGraph.cs` - Deterministic graph with sorted iteration

**Fields Layer (C)**
- `FieldProfile.cs` - PropagationRate, EdgeResistanceScale, DecayRate, LogAmp clamps
- `ScalarField.cs` - Multi-channel log-space field, neutral baseline = 1.0 (logAmp=0)
- `ChannelView.cs` - Lightweight facade for single channel
- `Propagator.cs` - Double-buffered deterministic propagation

**Core (C)**
- `Dimension.cs` - Container for graph + fields, no Unity deps

**Economy (E)**
- `ItemDef.cs` - Item definition with BaseValue
- `Economy.cs` - Availability + PricePressure fields, price sampling, trade injection

**Tests (D+E)**
- `FieldPropagationTests.cs` - 4 tests covering neutral baseline, order-independence, resistance, edge tags
- `EconomyTests.cs` - 2 tests covering neutral price, trade injection

## Guarantees Met

✅ No "Od" prefixes in class names
✅ Deterministic propagation (double-buffered)
✅ Stable iteration order (StringComparer.Ordinal everywhere)
✅ Neutral baseline = 1.0 multiplier (logAmp = 0)
✅ Sparse storage (missing keys = neutral)
✅ Log-space core
✅ Edge tags supported and filterable
✅ All tests green

## File Count
- Odengine core: 10 files
- Tests: 2 files
- Total: 12 clean files (down from 50+ broken files)

## Commits
1. Baseline before deletion
2. B+C: Clean graph and field layer
3. D: Propagator + propagation tests
4. (E merged into D)
