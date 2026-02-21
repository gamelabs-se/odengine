Below is the final, copy-pasteable instruction set to course-correct the agent. It assumes your rename: World → Dimension. It also enforces your rule: DELETE old junk, no transitional compatibility, no “keep for later”.

Summary of what must change from the agent’s current implementation
Delete (hard delete, remove files + references)
The agent currently has multiple overlapping field concepts and some order-dependent propagation. All of this goes:

OdWorld (already renamed to Dimension on your side, but delete the agent’s version if it exists)

OdField, Field base class (the abstract Field in the snippet)

ChanneledField

ScalarField as currently implemented (the “base amp + merge threshold + virtualization” one)

VirtualField / For(channelId) facade implementation as currently implemented

OdLayeredField

ChannelFieldStorage and any channel virtualization/merge/normalize logic

EconomyFields versions that create per-commodity fields (spice_availability, water.demand, etc.)

FieldPropagator versions that mutate the field in-place while iterating nodes (order dependent)

Unity example BasicFieldExample if it depends on the old API (recreate later if needed)

Replace with one unified core
You will implement exactly:

Dimension (nodes + edges + fields)

NodeGraph, Node, Edge

ScalarField (ONE implementation only)

ChannelView (lightweight facade over (ScalarField, channelId))

FieldProfile

Propagator (double-buffer / deterministic)

Economy wrapper (optional module) that uses two fields with channels: availability and pricePressure, and a sampler formula.

Key rule enforced
Amplitude is multiplicative.

Neutral baseline = 1.0 multiplier everywhere.

Internal storage uses log-space (logAmp), where neutral is 0.0, but the meaning is still multiplier 1.0.

✅ FINAL AGENT INSTRUCTIONS (copy-paste as is)
You are to implement the Odengine core field system from scratch as described below. Do not keep or “deprecate” old files. DELETE them and remove references. No compatibility shims. No transitional code.

Goals / Non-negotiables
One true field primitive: a scalar field over a graph with channels.

Determinism:

No order-dependent mutation during propagation.

Propagation must be double-buffered (read snapshot, write accumulators, apply once).

Stable iteration order: StringComparer.Ordinal sorting for nodes, edges, channels.

No “Od” prefixes in class names.

Neutral baseline everywhere:

All field samples default to multiplier = 1.0.

Sparse storage: if nothing stored → it is neutral (1.0).

Log-space core:

Internally store logAmp (float), neutral = 0.

Externally expose multiplier = exp(logAmp).

Step 0 — DELETE old code (must compile after each deletion step)
Hard delete these files/classes and anything referencing them:

OdLayeredField

ChanneledField

Field abstract base in the snippet

old OdField/OdWorld/OdNodeGraph variants (you are using Dimension, NodeGraph)

old ScalarField that contains merge/virtualization/normalization thresholds

old VirtualField implementation

old FieldPropagator that mutates in-place

any “EconomyFields” that creates per-commodity fields (like "water.demand" fields per item)

any storage type that implies “channel virtualization”, “merge thresholds”, “normalization”, etc.

If any of these are used in examples/tests, delete or rewrite the examples/tests too.

Step 1 — Implement the new core graph layer
Files / namespaces
Create these new files under Core/Graph/ (or whatever folder structure you prefer, but keep namespaces consistent):

Graph/Node.cs
Fields:

string Id

string Name optional

Does not store edges inline; edges belong to NodeGraph.

Graph/Edge.cs
Fields:

string FromId

string ToId

float Resistance (>= 0)

HashSet<string> Tags (edge tags required)

Behavior:

Tags are optional but supported.

Provide bool HasTag(string tag).

Graph/NodeGraph.cs
Stores:

Dictionary<string, Node> Nodes

Dictionary<string, List<Edge>> OutEdges (fromId → edges sorted)

Methods:

AddNode(Node node)

AddOrUpdateNode(Node node)

AddEdge(string fromId, string toId, float resistance, params string[] tags)

IReadOnlyList<Edge> GetOutEdgesSorted(string fromId) (always stable sort by ToId then tags)

IReadOnlyList<string> GetNodeIdsSorted()

Determinism rule: Every returned list must be stable sorted via StringComparer.Ordinal.

Step 2 — Implement the new field layer
Fields/FieldProfile.cs
Properties:

string ProfileId

float PropagationRate (multiplier applied to transmitted log energy or transmitted amount)

float EdgeResistanceScale

float DecayRate (applied to logAmp or multiplier—choose log-space implementation below)

float MinLogAmpClamp and float MaxLogAmpClamp (optional but recommended)

Defaults: [-20, +20] to avoid overflow

No “stabilization threshold”, no merge threshold, no normalization.

Fields/ScalarField.cs (ONE implementation only)
Represents a multi-channel scalar field.

string FieldId

FieldProfile Profile

Internal storage:

Dictionary<(string nodeId, string channelId), float logAmp> using a custom struct key (avoid tuple allocations if you want)

Sparse: if key missing → logAmp = 0 (multiplier 1.0)

Core methods:

float GetLogAmp(string nodeId, string channelId) → default 0

float GetMultiplier(string nodeId, string channelId) → exp(GetLogAmp(...))

void SetLogAmp(string nodeId, string channelId, float logAmp):

if abs(logAmp) < epsilon → remove key

clamp logAmp between profile clamps

void AddLogAmp(string nodeId, string channelId, float deltaLogAmp):

SetLogAmp(nodeId, channelId, GetLogAmp + delta)

ChannelView ForChannel(string channelId) → lightweight facade

Support stable enumeration for propagation:

IReadOnlyList<string> GetActiveNodeIdsSortedForChannel(string channelId)

IReadOnlyList<string> GetActiveChannelIdsSorted()

IEnumerable<(string nodeId, string channelId, float logAmp)> EnumerateAllActiveSorted()

Stable sort by channelId then nodeId.

Fields/ChannelView.cs
Lightweight wrapper around (ScalarField field, string channelId).

Methods:

float GetMultiplier(string nodeId) → delegate to field

float GetLogAmp(string nodeId)

void AddLogAmp(string nodeId, float deltaLogAmp)

void SetLogAmp(string nodeId, float logAmp)

No caching. No state.

Step 3 — Implement Dimension (your renamed World)
Core/Dimension.cs
Fields:

NodeGraph Graph

Dictionary<string, ScalarField> Fields

Methods:

Node AddNode(string id, string name = null)

void AddEdge(string fromId, string toId, float resistance, params string[] tags)

ScalarField AddField(string fieldId, FieldProfile profile)

ScalarField GetField(string fieldId)

ScalarField GetOrCreateField(string fieldId, FieldProfile profile)

No Unity dependencies.

Step 4 — Implement deterministic propagation (Propagator)
Fields/Propagator.cs
You must implement propagation as a double-buffer step:

Inputs
Dimension dimension

ScalarField field

float deltaTime

optional: Func<Edge,bool> edgeFilter OR (string requiredEdgeTag) OR both
(This is where edge tags matter: some fields can ignore certain edges.)

Behavior (log-space transmission)
For every active (channelId, nodeId) where logAmp != 0:

For each outgoing edge:

effectiveResistance = edge.Resistance \* field.Profile.EdgeResistanceScale

transmissionFactor = exp(-effectiveResistance)

transmittedLogAmpDelta = logAmp _ transmissionFactor _ field.Profile.PropagationRate \* deltaTime

NOTE: This is not physically perfect but is deterministic and stable.

Clamp the transmitted amount if needed.

Accumulate into pendingDeltas[(toNodeId, channelId)] += transmittedLogAmpDelta

After scanning all sources:

Apply decay to sources (optional):

logAmp _= (1 - DecayRate _ deltaTime) OR logAmp -= logAmp*DecayRate*deltaTime

Do NOT mutate during scan; schedule decay in pending deltas too.

Apply all pending deltas to the field:

stable order apply: sort keys by channelId then nodeId.

No in-place mutation during neighbor iteration.

Step 5 — Implement Economy as a thin wrapper (optional module but recommended now)
Economy/Economy.cs (or Economy/EconomyFieldSet.cs)
Create:

ScalarField availability = field "economy.availability" (channels = itemId)

ScalarField pricePressure = field "economy.pricePressure" (channels = itemId)

Price sampling:

availabilityMult = availability.ForChannel(itemId).GetMultiplier(nodeId)

pressureMult = pricePressure.ForChannel(itemId).GetMultiplier(nodeId)

price = baseValue \* pressureMult / max(availabilityMult, 0.0001f)

Trade injection (tiny ripple, deterministic):

Buying increases pricePressure locally and decreases availability locally:

availability.AddLogAmp(node, item, -k \* units)

pricePressure.AddLogAmp(node, item, +m \* units)
Choose constants k,m as parameters.

No “supply” or “demand” fields.

Step 6 — Tests (minimal but sufficient, must be green)
Create Tests/FieldPropagationTests.cs and implement:

NeutralBaseline_IsOneEverywhere

new Dimension, new field

no stored data

GetMultiplier(anyNode, anyItem) returns 1.0 (even if node exists)

Propagation_OrderIndependent

Build a graph A->B, A->C etc.

Set logAmp on A for channel "water"

Run propagation twice:

once iterating nodes in natural insertion order

once forcing reversed order in internal enumerator (you can simulate by adding in different order)

Result multipliers at B/C must match exactly (within tiny epsilon)

ResistanceBlocksTransmission

Same source amplitude, two edges with different resistances

Destination with higher resistance gets lower resulting multiplier

EdgeTags_FilterWorks

Add two edges A->B tagged "land" and A->C tagged "sea"

Propagate with requiredEdgeTag "land"

B changes, C does not

Economy_SamplePrice_NeutralEqualsBaseValue

availability=1, pressure=1 -> price==baseValue

Economy_TradeInjection_ChangesLocalPrice

apply injection

price at node increases deterministically

Keep tests fast, no Unity runtime.

Commit plan (must be multiple commits)
Commit A: delete old field systems + references until project compiles.

Commit B: graph layer (Node, Edge w/tags, NodeGraph).

Commit C: field layer (FieldProfile, ScalarField, ChannelView).

Commit D: Propagator with deterministic double buffer + tests 1–4.

Commit E: economy wrapper + tests 5–6.

Definition of “done”
Old deleted code is completely gone (no dead files).

The only field type is ScalarField.

Propagation is deterministic and double-buffered.

Edge tags exist and can be used to filter propagation.

Neutral baseline is 1.0 multiplier everywhere.

Tests are green.

Notes / constraints
Use StringComparer.Ordinal everywhere.

Avoid floating nondeterminism from unordered dictionary iteration: always sort keys before applying.

No “virtualization”, no merge threshold, no normalization.

If you want, paste back the agent’s current file list (or repo tree) and I can map exactly which files to delete and what to rename—but the spec above is already sufficient to steer implementation correctly with no technical debt.
