# Odengine

**A deterministic quantum-field-like simulation core for games.**

## What Is This?

Odengine is a simulation engine where the world is represented as **nodes embedded in scalar fields**. Game facts (prices, inventories, unit compositions) are **measurements/materializations** derived from those fields—deterministically, repeatably, and observer-dependently.

### Core Philosophy

- **Fields are primary**: Reality is not "objects with stats"—it's **fields with values** sampled at points
- **Measurement reveals**: A "price" or "inventory" is not stored—it's **computed on observation**
- **Determinism**: Same seed + same inputs = identical outcomes
- **Quantum-inspired**: Borrows the structural idea that fields + measurement are the right abstraction (not actual quantum physics)

## Architecture

### Core Concepts

1. **OdNode**: A point/anchor in the simulation (planet, city, ship, market)
2. **OdNodeGraph**: The topology layer (parent/child hierarchy)
3. **ScalarField**: Maps (FieldId × NodeId) → float (the "substance" of the universe)
4. **FieldStore**: Stores all scalar fields
5. **Observable**: Derived fields computed on measurement (e.g., price)
6. **MeasurementContext**: Deterministic observation context (seed, tick, observer, noise)
7. **MeasurementCache**: Ensures coherence within a tick (same query = same result)
8. **OdEngine**: Deterministic operators that evolve fields over time

### Field Types

**Stored Fields** (evolved by engines):
- `econ.availability.water` - resource density
- `war.intensity` - conflict pressure
- `influence.faction.f1` - faction power

**Derived Fields** (computed on measurement):
- `price.water` - function(availability, risk, war, policy)
- `threat.perceived` - function(influence, intel, distance)

### Components

Minimal, generic, numeric modifiers:
- **EmitterComponent**: Injects value into a field (mines, factories)
- **SinkComponent**: Removes value from a field (consumption, decay)
- **CouplingComponent**: (TODO) Field-to-field influence

## Example Usage

```csharp
// Create world
var world = new OdWorld(seed: 42);

// Build topology
world.Nodes.AddNode(new OdNode("galaxy1"));
world.Nodes.AddNode(new OdNode("system1", "galaxy1", "system"));
world.Nodes.AddNode(new OdNode("planet1", "system1", "planet"));

// Set initial field values
world.Fields.Set("econ.availability.water", "planet1", 100f);

// Register derived observable
var priceObs = new PriceObservable("water", "econ.availability.water", basePrice: 10f);
world.RegisterObservable(priceObs);

// Measure (observe) the price
var ctx = new MeasurementContext(world.WorldSeed, world.CurrentTick);
var price = world.Sample("price.water", "planet1", ctx);

// Add production
var components = new ComponentStore();
components.Add(new EmitterComponent("planet1", "econ.availability.water", rate: 5f));

// Evolve world
var engine = new EmissionEngine(components);
engine.Tick(world, delta: 1f);
world.AdvanceTick();

// Price changes deterministically
var newPrice = world.Sample("price.water", "planet1", 
    new MeasurementContext(world.WorldSeed, world.CurrentTick));
```

## Key Guarantees

1. **Determinism**: Same seed + same operations = identical results
2. **Coherence**: Within a tick, measuring the same thing twice returns the same value
3. **No hidden nondeterminism**: All iteration is sorted (StringComparer.Ordinal)
4. **Measurement is lazy**: Derived fields computed only when observed
5. **Shimmer**: Optional controlled noise for "quantum-like" variation (deterministic)

## Design Decisions

### Why Fields?

Traditional entity-component systems store discrete values ("this market has 50 water at 12cr each"). Field-based systems store **continuous distributions** ("availability density is 100 at this location"). This enables:
- Natural propagation/diffusion
- Lazy evaluation (compute only what's observed)
- Observer-dependent measurements (intel, fog-of-war)
- Emergent behavior from field interactions

### Why Observables?

Price/threat/perceived-stock are **projections**, not ground truth. Storing them creates synchronization nightmares. Computing them on-demand from stable fields is:
- Deterministic
- Cacheable per-tick
- Naturally consistent

### Why Measurement Cache?

Quantum mechanics: repeated measurement without system change yields same result. Games: opening a shop twice in one frame should show same prices. Cache ensures this without complex "last measurement time" tracking.

### Why Minimal Components?

Components should be **generic field modifiers**, not game-specific data ("ShipHullComponent" is wrong, "EmitterComponent" is right). This keeps the core universal and reusable.

## Status

**Phase**: Core POC  
**Complete**:
- Node graph topology
- Scalar field storage
- Measurement + caching + coherence
- Observables (derived fields)
- Deterministic noise (shimmer)
- Emission/Sink components
- Basic diffusion engine

**TODO**:
- Coupling components (field-to-field influence)
- Policy engine (modifiers, rules-as-data)
- Movement/pathing (OdEdgeGraph)
- War engine (field evolution + threshold events)
- Materializers (field → discrete units)
- Snapshot system (world serialization)

## Testing

Run tests in Unity Test Runner or via command line:
```bash
Unity -runTests -testPlatform PlayMode
```

All tests enforce:
- Determinism (same seed = same result)
- Coherence (cache consistency)
- Correctness (engine logic)

## License

MIT (or your choice)
