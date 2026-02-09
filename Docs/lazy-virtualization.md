# Lazy Channel Virtualization

## Core Concept

Fields have **two levels of amplitude**:

1. **Field Amplitude** – base scalar value per node (the "general field strength")
2. **Channel Amplitude** – realized layer when it deviates from field

This gives us:
- Conceptual model: "each item is a field"
- Runtime reality: one field with lazy-realized channels
- Scalability: only track what matters
- Determinism: clear fallback rules

---

## How It Works

### Field Amplitude (Base Layer)

Every `ScalarField` has base amplitude per node:

```csharp
field.SetFieldAmp("market_hub", 5.0f);
```

This affects ALL channels (items/factions/etc.) at that node **unless** they're explicitly tracked.

### Channel Amplitude (Realized Layer)

When you access a channel:

```csharp
float waterAvail = availabilityField.For("water").GetAmp("market_hub");
```

**If channel NOT tracked:** returns field amplitude (5.0)
**If channel IS tracked:** returns channel-specific amplitude

---

## Merge/Split Logic

### Split (Realization)

A channel becomes explicitly tracked when:
- `SetAmp()` or `AddAmp()` creates deviation > `MergeThreshold`
- Example: trade intent moves channel far from field

```csharp
// Field amp = 5.0
// Buy 50 water → availability drops to 4.0
// Deviation = 1.0 > MergeThreshold (0.1)
// → Channel "water" now explicitly tracked
```

### Merge (De-realization)

A channel stops being tracked when:
- Its amplitude gets within `MergeThreshold` of field amplitude
- Happens during `ProcessVirtualization()` each tick

```csharp
// Channel amp = 5.05, field amp = 5.0
// Deviation = 0.05 < MergeThreshold (0.1)
// → Channel removed, falls back to field
```

### Normalization

For channels that deviate **moderately** (< `NormalizationThreshold`):
- Pull toward field at `NormalizationRate` per tick
- Simulates market equilibrium forces
- Large spikes (> threshold) are preserved (game-meaningful)

---

## Configuration

```csharp
field.MergeThreshold = 0.1f;           // When to stop tracking
field.NormalizationThreshold = 2.0f;   // When to normalize
field.NormalizationRate = 0.1f;        // How fast to normalize
```

**MergeThreshold:** Lower = more channels tracked (precision vs performance)
**NormalizationThreshold:** Higher = preserve larger spikes
**NormalizationRate:** Higher = faster equilibrium (0-1 range)

---

## Economy Example

### Setup

```csharp
var economy = new EconomyEngine(dimension);
economy.RegisterItem(new ItemDef("water", baseValue: 100f));

// Set base market strength (affects all items)
economy.SetMarketStrength("market_hub", 5.0f);
```

### Before Trade

```csharp
// No trades yet, water uses field amplitude
float waterAvail = economy.Availability.For("water").GetAmp("market_hub");
// → 5.0 (field amplitude)

float price = economy.GetPrice("water", "market_hub");
// → baseValue * scarcityFunction(5.0)
```

### After Trade

```csharp
// Player buys 50 water
economy.ApplyTrade("water", "market_hub", 50f);

// Water channel now tracked (deviation significant)
float waterAvail = economy.Availability.For("water").GetAmp("market_hub");
// → ~4.0 (reduced by trade)

float price = economy.GetPrice("water", "market_hub");
// → baseValue * scarcityFunction(4.0) → higher price
```

### Propagation

```csharp
economy.Tick(1.0f);

// Water availability propagates to neighbors
// If deviation normalizes, channel may merge back
```

---

## Why This Design

✅ **Scalability:** Don't track 10,000 items explicitly everywhere
✅ **Conceptual clarity:** Still "each item is a field" in API
✅ **Game-meaningful:** Only track what matters (trades, events)
✅ **Deterministic:** Clear fallback rules, stable iteration
✅ **Dynamic:** Channels come and go based on game state
✅ **Memory efficient:** Sparse storage for realized channels

---

## Implementation Notes

### Virtual Field API

```csharp
VirtualField waterField = availabilityField.For("water");

// Reads channel if tracked, otherwise field
float amp = waterField.GetAmp(nodeId);

// Writes and realizes channel if deviation significant
waterField.SetAmp(nodeId, value);
waterField.AddAmp(nodeId, delta);
```

### Tick Sequence

```csharp
public void Tick(float dt)
{
    // 1. Apply intents (local amplitude changes)
    ApplyTradeIntents();
    
    // 2. Propagate fields (including realized channels)
    FieldPropagator.Step(availability, graph, dt);
    
    // 3. Process virtualization (merge/normalize)
    availability.ProcessVirtualization();
}
```

### Price Derivation

```csharp
public float GetPrice(string itemId, string nodeId)
{
    var item = GetItem(itemId);
    
    // Uses channel if tracked, field if not
    float availability = Availability.For(itemId).GetAmp(nodeId);
    
    if (availability < 0.01f)
        return item.BaseValue;
    
    // Scarcity formula
    float scarcityMultiplier = 1f / (1f + availability);
    return item.BaseValue * scarcityMultiplier;
}
```

---

## Testing Strategy

Key tests:
- ✅ Channel uses field amp when not tracked
- ✅ Channel realizes when deviation exceeds threshold
- ✅ Channel merges when close to field
- ✅ Normalization pulls toward field (moderate deviations)
- ✅ Large spikes preserved (no normalization)
- ✅ Trades affect local availability
- ✅ Propagation works for realized channels
- ✅ Determinism (same inputs = same outputs)
- ✅ Multiple items have independent channels

---

## Future Extensions

### Per-Item Profiles

```csharp
economy.SetItemOverride("rare_artifact", new ChannelProfileOverride
{
    PropagationRate = 0.1f,  // Propagates slower
    EdgeResistanceScale = 2.0f  // More affected by distance
});
```

### Demand/Supply Fields

Currently just availability. Can add:
- Demand field (consumption pressure)
- Supply field (production pressure)
- Price derived from all three

### Regional Policies

```csharp
// Tariffs, embargoes affect field propagation
regionNode.AddTrait("high_tariff");
// → affects EdgeResistanceScale for economy fields
```

---

## Summary

**Lazy channel virtualization** gives us:
- Pure scalar field mental model
- Item-specific behavior when needed
- Field-level behavior when sufficient
- Automatic merge/split based on game state
- Deterministic, scalable, testable

It's the "quantum superposition" concept done right:
- State exists abstractly (field amplitude)
- Collapses to specific (channel amplitude) when measured/perturbed
- Reverts to abstract when no longer distinct
