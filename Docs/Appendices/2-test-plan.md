Alright. Here’s a big, aggressive test suite plan that will stress the foundation like it owes you money.

I’m going to give you:

A test taxonomy (Core / Fields / Propagation / Economy / Determinism / Snapshots / Fuzz)

A huge list of specific tests (so it’s not vague)

Concrete NUnit test scaffolding + utilities you can paste in and expand

No legacy. No half measures.

0. Test structure (recommended)

Create folders like:

Tests/Core/Graph/

Tests/Core/Fields/

Tests/Core/Propagation/

Tests/Modules/Economy/

Tests/Determinism/

Tests/Fuzz/

Tests/Scenarios/

Tests/Snapshots/

Naming convention:

Graph_NodeGraphTests.cs

Fields_ScalarFieldTests.cs

Propagation_PropagatorTests.cs

Economy_EconomyTests.cs

Determinism\_\*

Fuzz\_\*

1. CORE: NodeGraph + Edge tags (lots of tests)
   Node basics

AddNode creates node retrievable by id

AddOrUpdateNode updates name, preserves id

GetNode throws vs returns null (test whichever you choose)

Duplicate AddNode behavior (throws or overwrites — lock it in)

NodeIdsSorted is ordinal-sorted and stable

Edge basics

AddEdge fails if from missing

AddEdge fails if to missing

AddEdge accepts resistance >= 0

AddEdge rejects negative resistance

AddEdge supports tags:

no tags = empty set

tags stored as ordinal-unique set

HasTag works

OutEdgesSorted order:

sorted by ToId ordinal, then by a stable tag order

Multiple edges from same FromId are all present (or if you enforce unique (from,to), test that rule)

Graph invariants

Graph with N nodes and M edges returns exactly M edges in out-adjacency

Graph never returns edges for unknown nodeId

Graph behaves deterministically regardless of insertion order

build same topology with different add order → NodeIdsSorted + OutEdgesSorted identical

Tag semantics

Edge with tag sea is filterable

Edge with tag land is filterable

Edge with multiple tags filterable by any single tag

2. FIELDS: ScalarField + ChannelView (tons)
   Baseline / sparsity

Missing node/channel returns neutral (multiplier = 1.0, logAmp = 0.0)

Setting logAmp ~ 0 removes key (sparse)

Setting logAmp to 0 removes key

AddLogAmp on missing creates key (unless delta ~ 0)

Clamp behavior:

logAmp clamps to Min/Max

extremely large inputs don’t produce NaN

Channel behavior

Independent channels don’t interfere

ChannelView delegates correctly

EnumerateAllActiveSorted is stable:

sort by channelId then nodeId ordinal

ActiveChannelIdsSorted is stable and ordinal

ActiveNodeIdsSortedForChannel is stable and ordinal

Algebra sanity

exp(logAmp) matches multiplier

multiplier composition property (within float error):

add logAmp == multiply multiplier

3. PROPAGATION: Deterministic double-buffer (this is where you win or die)
   Non-negotiable determinism tests

Order independence:

same graph + same initial field, but nodes inserted in different orders

run propagate once

final logAmp map identical (same active keys and values within epsilon)

Edge insertion independence:

same edges inserted in different order → identical results

Channel iteration independence:

create channels in different orders → identical results

Transmission correctness

resistance 0 transmits more than resistance 10

EdgeResistanceScale increases resistance effect

PropagationRate increases transmitted amount

deltaTime scales propagation linearly (or whatever rule you choose — lock it down)

No propagation if source logAmp == 0

Propagation respects edgeFilter/tagFilter

with requiredTag=land, sea edges transmit nothing

No in-place mutation (tests that catch order bugs)

Create a line A->B->C

Put amplitude only in A

Run one propagate step

Confirm C did NOT receive anything if your model is strictly 1-hop-per-step

If your model intentionally allows cascading in the same tick, then flip this test accordingly

This test is crucial: it tells you whether you accidentally used in-place updates.

Decay correctness

Decay reduces logAmp magnitude toward 0 (neutral)

Decay applied deterministically (same regardless of edge count)

Decay does not go past 0 incorrectly (depends on your implementation choice)

Numeric stability

Large graphs don’t blow to NaN

Large resistances don’t underflow into NaN

Very small values remain sparse (keys removed)

4. ECONOMY: Availability + PricePressure (big suite)

Assuming:

availabilityMult = exp(logAmp)

pressureMult = exp(logAmp)

price = baseValue \* pressureMult / max(availabilityMult, eps)

trade injects:

availability.AddLogAmp(node, item, -k \* units)

pressure.AddLogAmp(node, item, +m \* units)

Sampling

Neutral returns baseValue exactly

Higher pressure increases price

Higher availability decreases price

price monotonicity:

if pressure goes up and everything else constant → price goes up

if availability goes up → price goes down

eps guard works when availabilityMult is tiny

Trade injection tests

Buy increases price locally (immediately)

Buy decreases availability locally

Sell decreases pressure and increases availability (if you implement sell)

Tiny trade injection produces tiny ripple

Multiple trades same tick are additive and deterministic

Propagation integration

Pressure propagates differently than availability (if profiles differ)

Price changes at neighbor nodes after propagation steps

High resistance edges limit economic ripple

Edge tags:

availability crosses only trade edges

pressure crosses only info edges (if you do that)

or both share same tag filter — whichever rule you implement, test it

Multi-item isolation

Trades in item A do not affect item B

A node can have 1000 items without cross contamination

Stress: many nodes many items

100 nodes, 100 items, random injections → run 50 ticks → no NaNs, deterministic hash stable

5. DETERMINISM: The “same input => same output” hammer
   Determinism hash test

Create a deterministic “state hash”:

sort all active field entries (fieldId, channelId, nodeId)

hash floats using BitConverter on float or int representation of logAmp

include graph edges too

compare hash across runs

Tests:

Same initial seed, same steps → identical hash

Different seed → different hash

Different insertion order but same topology → identical hash

Replay tests

Record a series of injections (trades etc.) as a log

Apply to a fresh Dimension

Assert final hash identical

6. SNAPSHOTS: binary snapshots for large data sets

You said: binary snapshots, large datasets, debug over time.

Snapshot tests

Save snapshot then load snapshot yields identical hash

Snapshot size scales roughly with active entries (sparsity preserved)

Snapshot excludes neutral values (doesn’t store logAmp==0)

Cross-version forward compatibility (optional: version header)

Snapshot determinism:

snapshot bytes must be identical across runs if state is identical (key ordering stable!)

Also test:

snapshot writing uses stable ordering of keys

snapshot load respects ordinals and recreates exact sparse maps

7. FUZZ / PROPERTY TESTS (you want brutal confidence)

No need for external libraries; do deterministic PRNG.

Fuzz: random graph + random injections

Generate graph with N nodes, M edges, random resistances

Add K items

Run T ticks:

randomly inject trades

propagate both fields

Assert:

no NaNs in any stored logAmp

no infinities

hash stable across two runs with same seed

hash differs across different seeds

Adversarial fuzz

Massive resistance edges

Zero resistance edges

Highly connected hubs

Deep chain of nodes length 200

Star graph (1 hub, 999 leaves)

8. SCENARIO TESTS (long-run “game emulation”)

These are your “integration proof” tests.

Examples:

Island blockade scenario

two regions separated by sea edges with high resistance

sudden pressure spike on one side

verify slow diffusion to the other side

Trade route opening

add a new edge mid-simulation (if allowed)

verify ripple accelerates after edge exists

Market collapse

inject repeated buys until availability collapses

price skyrockets but stays finite and deterministic

Multi-faction pressure (if you later add faction channels)

“power” field channels by factionId

verify faction spikes don’t mix

Concrete code scaffolding (NUnit) — pasteable
A) Deterministic PRNG for fuzz
public sealed class DeterministicRng
{
private uint \_state;
public DeterministicRng(uint seed) { \_state = seed == 0 ? 1u : seed; }

    public uint NextU()
    {
        // xorshift32
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    public int NextInt(int min, int max)
    {
        if (max <= min) return min;
        uint range = (uint)(max - min);
        return (int)(NextU() % range) + min;
    }

    public float NextFloat(float min, float max)
    {
        uint v = NextU();
        float t = (v / (float)uint.MaxValue);
        return min + (max - min) * t;
    }

    public bool NextBool(float trueProb = 0.5f) => NextFloat(0f, 1f) < trueProb;

}
B) State hash utility (use everywhere)
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class StateHash
{
public static string Compute(Dimension dim)
{
// Stable string builder (fast enough for tests; later you can optimize)
var sb = new StringBuilder();

        // Graph
        foreach (var nodeId in dim.Graph.GetNodeIdsSorted())
            sb.Append("N:").Append(nodeId).Append('\n');

        foreach (var fromId in dim.Graph.GetNodeIdsSorted())
        {
            var edges = dim.Graph.GetOutEdgesSorted(fromId);
            foreach (var e in edges)
            {
                sb.Append("E:")
                  .Append(e.FromId).Append("->").Append(e.ToId)
                  .Append("|r=").Append(e.Resistance.ToString("R"))
                  .Append("|tags=").Append(string.Join(",", e.Tags.OrderBy(x => x, StringComparer.Ordinal)))
                  .Append('\n');
            }
        }

        // Fields
        foreach (var fieldId in dim.Fields.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var field = dim.Fields[fieldId];
            sb.Append("F:").Append(fieldId).Append('\n');

            foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
            {
                // Use bitwise float identity
                int bits = BitConverter.SingleToInt32Bits(logAmp);
                sb.Append("A:")
                  .Append(channelId).Append("|").Append(nodeId)
                  .Append("|").Append(bits)
                  .Append('\n');
            }
        }

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

}
C) One killer determinism test (template)
[Test]
public void Determinism_SameSeedSameOutputHash()
{
string Run(uint seed)
{
var rng = new DeterministicRng(seed);
var dim = TestBuilders.BuildRandomDimension(rng, nodeCount: 50, edgeCount: 200);

        var availability = dim.AddField("economy.availability", TestProfiles.Availability());
        var pressure = dim.AddField("economy.pricePressure", TestProfiles.Pressure());

        // Seed some amplitudes
        for (int i = 0; i < 200; i++)
        {
            var nodeId = $"n{rng.NextInt(0, 50)}";
            var itemId = $"item{rng.NextInt(0, 25)}";

            availability.AddLogAmp(nodeId, itemId, rng.NextFloat(-0.02f, 0.02f));
            pressure.AddLogAmp(nodeId, itemId, rng.NextFloat(-0.02f, 0.02f));
        }

        // Run ticks
        for (int t = 0; t < 20; t++)
        {
            // Random trade injections
            for (int k = 0; k < 30; k++)
            {
                var nodeId = $"n{rng.NextInt(0, 50)}";
                var itemId = $"item{rng.NextInt(0, 25)}";
                int units = rng.NextInt(1, 10);

                // Inject (constants from profile or config)
                availability.AddLogAmp(nodeId, itemId, -0.001f * units);
                pressure.AddLogAmp(nodeId, itemId, +0.001f * units);
            }

            Propagator.Step(dim, availability, deltaTime: 1f, requiredEdgeTag: "trade");
            Propagator.Step(dim, pressure, deltaTime: 1f, requiredEdgeTag: "info");
        }

        return StateHash.Compute(dim);
    }

    var h1 = Run(123);
    var h2 = Run(123);
    Assert.AreEqual(h1, h2);

}
“Huge suite” checklist (copy into a tracking doc)

If you want it truly massive, aim for:

Graph tests: ~40

Field tests: ~50

Propagation tests: ~80

Economy tests: ~60

Determinism tests: ~25

Snapshot tests: ~30

Fuzz tests: ~20 (fast ones)

Scenario tests: ~15

Total: ~320 tests. That’s a foundation you can actually trust.

Next step (what I’d do right now)

Before adding War/Combat/etc, lock the foundation by implementing three test layers:

Unit-level correctness (Graph/Field/Propagator)

Determinism & replay hashing

Snapshots (binary)

Once those are green, everything else becomes safe to build.

If you paste your current Dimension / NodeGraph / ScalarField / Propagator APIs (method names), I’ll rewrite the scaffolding above to match your exact signatures so you can paste tests in immediately with zero friction.
