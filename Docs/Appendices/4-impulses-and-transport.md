1. Formalize Impulses: do we need an ImpulseLog?

Yes — but not as part of simulation state. It should be a deterministic event stream you can optionally persist/replay, and it should be separable from snapshots.

The clean model (recommended)

You have three layers:

State

Dimension (graph)

ScalarFields (logAmp per (node, channel))

Nothing else.

Impulse stream (optional, external)

A record of “what happened” as inputs to the state.

Snapshots (optional, external)

Binary serialized state at certain ticks for debugging/time travel.

This gives you:

Deterministic sim

Perfect replay (if you keep impulses)

Cheap jumping (if you keep snapshots)

No technical debt inside the core

ImpulseLog format (what it must contain)

Minimum fields to guarantee reproducibility:

Tick (long)

Sequence (int) — stable ordering within a tick

FieldId (string)

NodeId (string)

ChannelId (string) — item/faction/etc

DeltaLogAmp (float)

ReasonCode (string or small int) — optional, for debugging

OriginId (string) — optional (player id, AI id, system id)

Important: the sim should apply impulses in a deterministic order:
Tick, then FieldId, then NodeId, then ChannelId, then Sequence (or Sequence last if you want “original order wins”).

How do we use it?

Two modes:

A) Pure live mode

Game generates impulses each tick, applies them immediately.

No impulse storage.

B) Record/replay mode

Every applied impulse is appended to ImpulseLog.

Replays apply the exact same impulse list.

C) Hybrid (best for big games)

Keep snapshots every N ticks (e.g. every 100).

Keep impulse log for the whole run.

For debugging: load latest snapshot ≤ target tick, then replay impulses forward.

What about propagation?

Propagation is state evolution; it does not need to be logged if it’s deterministic given:

State

Propagation parameters

Tick count

So your log is only impulses, not derived updates.

2. Cross-field propagation (coupling): engine-core or game layer?

You can do it inside the engine without polluting it, if you keep it generic and declarative.

Option 1 (recommended): “CouplingRules” inside Odengine, but purely generic

Add a small subsystem:

CouplingRule

reads one or more fields (at node/channel)

writes impulses into one or more other fields

deterministic

tag-gated

Example (economy-like):

“High productivity increases availability:food”

“High power increases pricePressure:weapons”

“Low availability:water increases pricePressure:water”

This stays engine-agnostic if you define it as:

input field IDs

output field IDs

a formula (choose a small set of built-in function types, not arbitrary code)

Do NOT use callbacks in core if you care about determinism across platforms/builds. Callbacks quickly become “hidden non-determinism.”

Minimal built-in coupling function types:

linear: out += k \* in

clamp: out += k \* clamp(in, a, b)

ratio: out += k \* (a / (b + eps))

threshold: if in > t then out += k

tag multiplier: k \*= TagFactor(node, "industrial")

This gives you a “macro environment” that is data-driven but still core-owned and testable.

Option 2: game layer handles coupling (works, but you lose purity)

Game reads sampled values and emits impulses into other fields.
This is fine if you accept:

coupling logic lives outside the deterministic core

you must version/control it carefully

it’s harder to test at the engine level

If your goal is “Odengine is the deterministic quantum sim engine,” coupling rules belong in Odengine as data, not game code.

✅ My recommendation: CouplingRules live in Odengine, but only as a small declarative system.

3. “We don’t need a travel/logistics engine anymore right?”

You still need something that models transport constraints if you want oceans / restricted areas / edges to matter economically or militarily.

But it should not be “a logistics engine with inventories and routes” unless you want that.

In the field model, transport becomes:

edge resistance

field-specific resistance scaling

node rejection

tag filters

That’s already a transport model.

So you can delete the old notion of “LogisticsEngine moving goods” and instead do:

Availability propagates across edges with resistance

War/power propagates differently (different profile)

“Ocean” is just a region of edges with high resistance for some fields

“Troop transports” is simply: power field has lower resistance across ocean edges than availability does (or uses a different profile / scaling)

So the answer is:

✅ You likely do not need the old logistics engine concept.

✅ You do still need propagation + resistance + rejection rules (that is transport).

✅ If you later want explicit shipping lanes / trade routes, that becomes: “special low-resistance edges” or “temporary edges created by infrastructure impulses.”

In other words: logistics becomes graph topology + resistances + profiles, not a separate engine that pushes units.
