# Appendix I — Fields, Amplitude, Resistance, and Echoes

**(The Core Mental Model)**

This appendix explains the **core mental image** behind Odengine's simulation model, focusing on **scalar fields**, **amplitude**, **edges**, and **resistance**. These concepts apply universally across economy, war, influence, availability, power, etc. No special cases. No domain-specific hacks.

---

## 1. The World Is a Set of Fields Over Nodes

Odengine does **not** store "prices", "stock", "army sizes", or "political power" as hard state.

Instead:

* The world consists of **OdNodes** (everything is a node)
* Over that graph exist **scalar fields**
* Each field assigns a **scalar value** at each node

These scalar values are called:

> **Amplitude (Amp)**

Amplitude is *how much* of a field exists at a location.

Examples:

* Economy → Availability field → `Amp = how present this item is`
* Economy → Price field → `Amp = price pressure`
* War → Power field → `Amp = military force density`
* Politics → Influence field → `Amp = political pressure`
* Social → Unrest field → `Amp = instability pressure`

Amplitude is just a float. Nothing more. Nothing less.

---

## 2. Fields Are Like the Force (Mental Model)

A good mental image is **the Force** from Star Wars:

* It exists everywhere
* It is stronger in some places than others
* It flows through the world
* Geography and structure affect how it spreads
* Some things resist it, some conduct it

But unlike mysticism, Odengine's fields are:

* Deterministic
* Numeric
* Explicitly computed
* Fully inspectable

No magic. Just math.

---

## 3. Amplitude (Amp) — What It Replaces

Amplitude replaces many overloaded ideas:

| Old Concept | Replaced By |
| ----------- | ----------- |
| Value       | Amp         |
| Strength    | Amp         |
| Stock       | Amp         |
| Power       | Amp         |
| Influence   | Amp         |
| Supply      | Amp         |
| Pressure    | Amp         |

**Amp is not a budget.**
It is **density / presence / intensity**.

Budgets, inventories, units, items, armies — those are **realized representations** created by the game layer when sampling Amp.

Odengine never cares *what* you realize Amp into.

---

## 4. Fields Do Not Store "Stuff"

A crucial design decision:

> **Fields do not store objects, items, or entities.**

They store only **scalar amplitudes**.

This solves:

* Infinite storage problems
* Bookkeeping explosion
* Synchronization bugs
* "What happens when you're not looking?"

### Example: Availability vs Stock

* Availability field has `Amp = 0.82` at a city
* Game layer samples that and realizes:
  * "This market has ~12 units of rare spice"
* Close the market UI → those units disappear
* Open again same tick → exact same result
* Open 10 ticks later → slightly different result

No stock persisted. Only the field exists.

---

## 5. Sampling a Field (Measurement)

Sampling a field:

* Reads Amp at `(node, field, layer)`
* Is deterministic
* Does **not** mutate the field directly

However…

### Observation Has Consequences (Indirectly)

Sampling often causes **decisions**:

* AI decides to trade
* Player sends fleet
* War escalates
* Production increases

Those actions inject **changes** back into fields through engines.

This mirrors quantum mechanics:

* Measurement does not "collapse the field"
* But actions taken because of measurement alter future states

Odengine itself remains pure and deterministic.

---

## 6. Nodes Shape Fields

Nodes do not "own" fields.
They **shape** them.

Via:

* Tags
* Components
* Field profiles

Examples:

* Market node shapes Availability and Price fields
* Factory node injects Availability
* Capital node amplifies Influence
* Fortress node resists War Power
* Holy site amplifies "Force-like" fields

Nodes are terrain. Fields flow through terrain.

---

## 7. Edges: How Fields Move

Fields propagate across the **node graph**.

Edges are **the only way fields move between nodes**.

An edge has exactly one core property:

> **Resistance**

That's it.

No special cases.

---

## 8. Resistance — The Only Transport Mechanic

**Resistance** describes how difficult it is for *something* to pass through an edge.

But the key insight:

> Resistance is interpreted **per field**.

### Effective Resistance

Each field defines how much it cares about resistance.

```
R_eff = Edge.Resistance × Field.EdgeResistanceScale
```

Then transmission is computed as:

```
TransmittedAmp = Amp × exp(-R_eff)
```

This gives smooth, natural attenuation.

---

## 9. Why This Is Powerful

### Example: Oceans

Edge:

```
Resistance = 100
Tags = { "ocean" }
```

Fields:

| Field              | EdgeResistanceScale | Result           |
| ------------------ | ------------------- | ---------------- |
| Army Power         | 1.0                 | Blocked          |
| Trade Availability | 0.4                 | Partially passes |
| Cultural Influence | 0.1                 | Mostly passes    |
| "Force"            | 0.0                 | No resistance    |

Same edge.
Different behavior.
Zero special casing.

---

## 10. Rejection Without Hard Locks

This gives **rejection**, not soft locks.

* Want to block armies? → high resistance × high scale
* Want oceans to be crossable with effort? → add port nodes with low resistance edges
* Want certain fields to ignore geography? → scale = 0

No binary rules.
Just math.

---

## 11. Echoes (Reframed Correctly)

An **Echo** is not a special object.

An echo is:

> A change in amplitude that propagates outward over time.

That's it.

Wars, disasters, discoveries, plagues, miracles:

* Change Amp at origin
* Engines propagate fields
* Nearby nodes feel it
* Distant nodes feel it less
* Over time it diffuses or stabilizes

No "echo engine".
No special data structure.
Echoes are emergent behavior.

---

## 12. Field Profiles (How Fields Behave)

Each field defines its own **FieldProfile**:

* Propagation rate
* Resistance sensitivity
* Stabilization / decay rate
* Interaction with node tags
* Interaction with other fields (optional)

Fields are peers.
No field is privileged.

---

## 13. Why This Matches Your Goals

✔ Deterministic  
✔ Abstract  
✔ Minimal  
✔ Extensible  
✔ No legacy constraints  
✔ No hard-coded mechanics  
✔ No object bookkeeping  
✔ Quantum-inspired without being fake physics  

Most importantly:

> **Everything feels alive without being simulated explicitly.**

---

## 14. Summary in One Sentence

**Odengine simulates a universe as scalar fields flowing over a graph, shaped by nodes, attenuated by resistance, sampled into reality only when observed — with all complexity emerging from simple, universal rules.**
