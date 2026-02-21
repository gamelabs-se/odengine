On game data being outside of Odengine:
100% — Odengine must not ship any semantic IDs like water, weapons, food, pirates, etc.

When I said “field IDs,” I meant string identifiers provided by the game, not predefined by Odengine. The core only defines the mechanism.

What Odengine may define vs must not define
Odengine may define (core)

The concept of FieldId / ChannelId as opaque strings

The concept of CouplingRule that maps (FieldId, ChannelId) → (FieldId, ChannelId)

A small set of built-in math operators (linear, clamp, threshold, ratio…)

Deterministic scheduling and execution order

Optional “templates” as data structures (not content)

Odengine must not define (game content)

Any specific IDs: water, iron, pirate, empire

Any tags with meaning: planet, market, ocean (unless you’ve explicitly decided tags are also game-defined—recommended)

Any domain assumptions: economy/war/etc

So yes: unit tests can use explicit IDs (they have to), but core cannot ship meaningful defaults.

The clean pattern

1. Everything is addressed by opaque keys

FieldId: "economy.availability" (game-defined)

ChannelId: "item:water" or "water" (game-defined)

Tag: "ocean" / "land" / "road" (game-defined)

Odengine just treats them as strings. No semantics.

2. Coupling rules are generic, not semantic

A coupling rule never says “water price.” It says:

“Map from input field A/channel selector X to output field B/channel selector Y using function F.”

Where selectors are also generic.

Example, expressed generically:

Input: Field="availability", ChannelSelector="\*", value sampled at node

Output: Field="pricePressure", ChannelSelector="sameChannel"

Function: out += k \* (-log(1 + in)) (or whatever)

Nothing about “water” is hardcoded. The game decides what channels exist.

How to avoid “IDs everywhere”: selectors + templates

If you don’t want coupling rules written per-item, you add Channel Selectors:

"\*" (all channels that currently exist in a field)

"same" (same channel id as input)

"tagged:item" (if you allow channel tags via registry; optional)

"explicit:[a,b,c]" (small list)

Then a single coupling rule can apply to all items.

Example template (still content-free)

“Scarcity influences price pressure” template:

from availability to pricePressure

applies to all channels

formula is generic

The game instantiates this template by providing the actual field IDs.

Where do “meaningful IDs” live?

In the game’s bootstrap layer (or “simulation configuration” file), not in Odengine.

Something like:

Economy.AvailabilityFieldId = "economy.availability"

Economy.PricePressureFieldId = "economy.price"

item list / channel list is game-defined

Odengine never ships those strings.

Testing stance (what you said is exactly right)

✅ Tests should use concrete IDs: "economy.availability", "water", "iron"

✅ Core should never contain those IDs outside tests/examples

✅ Examples should live in example assemblies/namespaces and be clearly non-core

If you want, the extra step is enforcing this with repo rules:

Odengine.Core assembly: no references to game IDs; no example content

Odengine.Tests / Odengine.Examples: free to use IDs

Small recommendation: define a “Registry” but keep it optional

To support selectors like “all items,” add:

ChannelRegistry (tracks which channels exist per field)

or derive channels from field storage itself (active channels)

No semantics; just introspection.

Yes, I feel you. The right mental model is:

Odengine = physics + math + determinism + storage
Game = meaning + naming + content + mapping to visuals

If you tell me whether you want tags to also be 100% game-defined (I recommend yes), I’ll lock the coupling rule shape and execution order so it’s impossible for semantics to creep into core.
