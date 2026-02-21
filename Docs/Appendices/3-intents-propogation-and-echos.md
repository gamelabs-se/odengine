You’ve already made a crucial architectural decision:

Effects are applied instantly at origin.
Propagation takes time.

That’s clean. That’s deterministic. That’s sane.

Now let’s define the concepts precisely and stop hand-waving.

First: What are we actually modeling?

You have:

A Dimension (graph of nodes + edges)

A set of ScalarFields

Each field stores log-amplitude per (node, channel)

When something happens (trade, battle, production, etc.), you:

Modify amplitude at a node immediately.

Over time, propagation diffuses that modification through edges.

So conceptually:

A local disturbance.

Then spatial diffusion.

Then decay.

That’s it.

Everything else is visualization.

The Naming Problem

You are currently juggling:

Intent

Impulse

Wave

Echo

Let’s dissect each and decide properly.

1️⃣ Intent (Old System)

Old model:

Intent = request

Engines later actualize it

You’re abandoning that model.

You now want:

When something happens, it modifies the field immediately.

So "intent" no longer fits. Intent implies deferred resolution.

❌ Retire “Intent” at engine-core level.
You may still use it in game layer for player/AI decisions.

2️⃣ Impulse

Physics meaning:
An impulse is a sudden localized change in a field.

That matches perfectly:

Trade = local impulse in availability + pricePressure.

Battle = local impulse in power field.

Production = local impulse in availability field.

Impulse is:

Instant

Local

Not propagated yet

Just a delta to logAmp

That is exactly what you’re doing.

✅ Use “Impulse” for the immediate field modification.

Definition:

An Impulse is a deterministic local modification of a ScalarField at a specific node and channel.

It does not travel.
It does not diffuse.
It does not store history.
It just changes the state.

3️⃣ Wave

Now this is where people get fuzzy.

In real physics:

A wave is a propagating oscillation.

You are NOT implementing oscillation (unless you explicitly add that).

Your system is:

Diffusion-like.

Exponential attenuation over edges.

Deterministic propagation.

No sinusoidal behavior (unless you add it in profile).

So “wave” is not technically correct unless oscillation exists.

However…

You are visually modeling:

A disturbance spreading.

Amplitude decaying.

Influence rippling outward.

That is more accurately:

A propagating disturbance.

Which in physics can be:

Wave

Diffusion

Field propagation

But you're not solving wave equations.

So:

❌ Don’t use “Wave” in code.
It implies oscillation and PDE complexity you’re not modeling.

4️⃣ Echo

This is where your intuition is strongest.

An echo is:

A disturbance that continues to exist after the original event.

A fading imprint.

A propagated memory.

That is exactly what your system produces.

Impulse → propagation → fading trace across nodes.

That trace is:

An Echo of the impulse.

And crucially:

You are not storing echo objects.
You are not tracking history.
The echo is simply the current field amplitude.

That’s beautiful.

So:

Impulse = cause

Echo = resulting distributed field state

No extra system required.

The Clean Conceptual Model

Let’s lock it in:

🔹 Impulse

Immediate change to a field at one location.

Examples:

Buy 5 units → impulse in availability & pricePressure

Battle won → impulse in power field

Factory produces → impulse in availability field

Technical:

field.AddLogAmp(nodeId, channelId, delta);
🔹 Propagation

Deterministic step that spreads logAmp across edges.

Not called wave.
Not called echo engine.
Just:

Field propagation.

🔹 Echo

Not a class.

Not a stored entity.

The echo is:

The spatial distribution of amplitude after propagation steps.

So when you sample:

availability.GetMultiplier(nodeId, itemId)

You are sampling the echo of all past impulses.

That’s it.

No need to represent echo explicitly.

Final Vocabulary Decision (Strong Recommendation)

At engine-core level:

Concept Name
Immediate local change Impulse
Stored field state Amplitude
Multiplier view Multiplier
Spreading behavior Propagation
Distributed aftermath Echo (conceptual only)

No Wave class.
No Echo class.
No Intent class (in core).

Example Flow (Concrete)

Player buys spice at City A:

Game layer:

availability.AddLogAmp("cityA", "spice", -0.002f);
pricePressure.AddLogAmp("cityA", "spice", +0.002f);

That is an Impulse.

Next tick:

Propagator.Step(dimension, availability);
Propagator.Step(dimension, pricePressure);

Neighbor cities now have slight non-neutral logAmp.

That distribution = the Echo of the trade.

Why This Is Clean

You don’t store impulses.

You don’t store echoes.

You don’t track waves.

You only store amplitude.

Time + propagation creates echo naturally.

This aligns with:

Your deterministic philosophy

Your quantum-inspired field model

No special-case systems

No “echo engine”

Important Clarification

You said:

I prefer them being applied instantly, and propagating them takes time.

That means:

Impulse = immediate state mutation
Echo = natural consequence of propagation over time

This is exactly how it should be.

One More Layer of Precision (Optional But Elegant)

If you want conceptual perfection:

You can define:

Impulse = delta in logAmp
Echo = non-zero logAmp outside the origin over time

You never need to formalize echo beyond that.

Final Answer

Use:

Impulse — for instantaneous local field change.

Propagation — for deterministic spreading.

Echo — as a conceptual term for the distributed result.

Do NOT use:

Intent (in core)

Wave (unless you implement oscillation)

EchoEngine

WaveObject

You now have a clean physics-consistent mental model.
