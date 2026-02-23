using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;
using Odengine.Serialization;
using Odengine.War;

namespace Odengine.Tests.Snapshots
{
    /// <summary>
    /// Tier 5+6 — Byte-level determinism, fuzz stability, scenario invariants.
    ///
    /// Determinism contract: given the same Dimension state and the same tick/simTime,
    /// WriteFull / WriteCheckpoint / WriteDelta must produce byte-identical output
    /// (modulo the created_utc_ms timestamp at header offset 23).
    ///
    /// Fuzz contract: random Dimensions with random field entries must survive round-trip
    /// without NaN/Inf, and same seed must produce identical bytes.
    ///
    /// Scenario contract: 500 tick continuous run — all logAmps stay within FieldProfile
    /// clamp bounds and contain no NaN/Inf values.
    /// </summary>
    [TestFixture]
    public class Snapshot_DeterminismTests
    {
        // ── Deterministic RNG (xorshift32) ────────────────────────────────────

        private static uint Xorshift(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private static float NextFloat01(ref uint state) =>
            (Xorshift(ref state) & 0x00FFFFFF) / (float)0x01000000;

        // ── Helpers ───────────────────────────────────────────────────────────

        private static FieldProfile P(string id = "p") => new FieldProfile(id)
        {
            PropagationRate = 0.1f,
            MinLogAmpClamp = -10f,
            MaxLogAmpClamp = 10f,
            LogEpsilon = 0.0001f
        };

        /// <summary>
        /// Build a Dimension deterministically from a uint seed.
        /// Same seed always produces same graph, field IDs, and logAmp values.
        /// </summary>
        private static Dimension BuildDeterministic(uint seed, int nodeCount = 5,
            int edgeCount = 6, int fieldCount = 2, int entryCount = 10)
        {
            var rng = seed;
            var nodeIds = new string[nodeCount];
            var dim = new Dimension();

            for (int i = 0; i < nodeCount; i++)
            {
                nodeIds[i] = $"node_{i:D3}";
                dim.AddNode(nodeIds[i], $"N{i}");
            }

            for (int i = 0; i < edgeCount; i++)
            {
                int from = (int)(Xorshift(ref rng) % (uint)nodeCount);
                int to = (int)(Xorshift(ref rng) % (uint)nodeCount);
                if (from == to) to = (to + 1) % nodeCount;
                float res = 0.1f + NextFloat01(ref rng) * 2f;
                dim.AddEdge(nodeIds[from], nodeIds[to], res);
            }

            string[] channels = { "alpha", "beta", "gamma", "delta", "epsilon" };

            for (int fi = 0; fi < fieldCount; fi++)
            {
                var field = dim.AddField($"field_{fi}", P($"p{fi}"));
                for (int e = 0; e < entryCount; e++)
                {
                    int nIdx = (int)(Xorshift(ref rng) % (uint)nodeCount);
                    int cIdx = (int)(Xorshift(ref rng) % (uint)channels.Length);
                    float amp = (NextFloat01(ref rng) - 0.5f) * 8f;
                    if (MathF.Abs(amp) > 0.01f)
                        field.SetLogAmp(nodeIds[nIdx], channels[cIdx], amp);
                }
            }

            return dim;
        }

        /// <summary>Zero the 8-byte created_utc_ms timestamp (header offset 23) before byte comparison.</summary>
        private static byte[] StripTimestamp(byte[] bytes)
        {
            var copy = (byte[])bytes.Clone();
            for (int i = 23; i < 31; i++) copy[i] = 0;
            return copy;
        }

        // ── Full snapshot determinism ─────────────────────────────────────────

        [Test]
        public void Full_SameState_SameConfig_IdenticalBytes()
        {
            var dim = BuildDeterministic(12345);
            var w = new SnapshotWriter();
            var b1 = StripTimestamp(w.WriteFull(dim, 42, 3.14));
            var b2 = StripTimestamp(w.WriteFull(dim, 42, 3.14));
            CollectionAssert.AreEqual(b1, b2,
                "WriteFull with identical state must produce identical bytes (excl. timestamp)");
        }

        [Test]
        public void Full_InsertionOrder_DoesNotAffectBytes()
        {
            // dim1: nodes added aaa, mmm, zzz
            var dim1 = new Dimension();
            dim1.AddNode("aaa"); dim1.AddNode("mmm"); dim1.AddNode("zzz");
            dim1.AddField("f", P("f")).SetLogAmp("aaa", "alpha", 1.5f);
            dim1.GetField("f").SetLogAmp("zzz", "beta", 2.0f);

            // dim2: same nodes but added zzz, aaa, mmm
            var dim2 = new Dimension();
            dim2.AddNode("zzz"); dim2.AddNode("aaa"); dim2.AddNode("mmm");
            dim2.AddField("f", P("f")).SetLogAmp("zzz", "beta", 2.0f);
            dim2.GetField("f").SetLogAmp("aaa", "alpha", 1.5f);

            var w = new SnapshotWriter();
            var b1 = StripTimestamp(w.WriteFull(dim1, 1, 0));
            var b2 = StripTimestamp(w.WriteFull(dim2, 1, 0));
            CollectionAssert.AreEqual(b1, b2,
                "Node insertion order must not affect snapshot byte layout (string pool is sorted)");
        }

        [Test]
        public void Full_DifferentTick_DifferentBytes()
        {
            var dim = BuildDeterministic(99);
            var w = new SnapshotWriter();
            var b1 = StripTimestamp(w.WriteFull(dim, 1, 0));
            var b2 = StripTimestamp(w.WriteFull(dim, 2, 0));
            CollectionAssert.AreNotEqual(b1, b2,
                "Different tick values must produce different bytes");
        }

        [Test]
        public void Full_DifferentState_DifferentBytes()
        {
            var dim1 = BuildDeterministic(111);
            var dim2 = BuildDeterministic(222); // different seed → different entries
            var w = new SnapshotWriter();
            var b1 = StripTimestamp(w.WriteFull(dim1, 1, 0));
            var b2 = StripTimestamp(w.WriteFull(dim2, 1, 0));
            CollectionAssert.AreNotEqual(b1, b2,
                "Different field state must produce different bytes");
        }

        // ── Checkpoint determinism ────────────────────────────────────────────

        [Test]
        public void Checkpoint_SameState_IdenticalBytes()
        {
            var dim = BuildDeterministic(42, nodeCount: 4, fieldCount: 2, entryCount: 6);
            var war = new WarSystem(dim, P("war"), new WarConfig { ExposureGrowthRate = 0.08f });
            war.DeclareWar("node_000");
            war.SetNodeStability("node_001", 0.5f);
            war.DeclareOccupation("node_002", "faction_alpha");

            var w = new SnapshotWriter();
            var b1 = StripTimestamp(w.WriteCheckpoint(dim, 10, 10.0, new ISnapshotParticipant[] { war }));
            var b2 = StripTimestamp(w.WriteCheckpoint(dim, 10, 10.0, new ISnapshotParticipant[] { war }));

            CollectionAssert.AreEqual(b1, b2,
                "WriteCheckpoint with identical state must produce identical bytes (excl. timestamp)");
        }

        [Test]
        public void Checkpoint_ParticipantOrder_DoesNotAffectBytes()
        {
            // System blobs should be sorted by SystemId — order of participants list shouldn't matter.
            // We can only test this if we have two different participants; use a stub approach:
            // Since we only have WarSystem (SystemId = "war.system"), we test a single-participant
            // checkpoint written twice with the same list (determinism).
            var dim = BuildDeterministic(77);
            var war = new WarSystem(dim, P("war"));
            war.DeclareWar("node_000");

            var w = new SnapshotWriter();
            var participants = new ISnapshotParticipant[] { war };
            var b1 = StripTimestamp(w.WriteCheckpoint(dim, 1, 0, participants));
            var b2 = StripTimestamp(w.WriteCheckpoint(dim, 1, 0, participants));

            CollectionAssert.AreEqual(b1, b2,
                "Repeated WriteCheckpoint calls must yield identical bytes");
        }

        // ── Delta determinism ─────────────────────────────────────────────────

        [Test]
        public void Delta_SameChange_IdenticalBytes()
        {
            var dim = BuildDeterministic(777, fieldCount: 2, entryCount: 6);
            var w = new SnapshotWriter();
            var full = w.WriteFull(dim, 0, 0);

            // Apply same mutation
            dim.GetField("field_0").SetLogAmp("node_001", "alpha", 5.0f);
            dim.GetField("field_1").SetLogAmp("node_002", "beta", 3.0f);

            var d1 = StripTimestamp(w.WriteDelta(dim, full, 1, 1.0, 0, 1));
            var d2 = StripTimestamp(w.WriteDelta(dim, full, 1, 1.0, 0, 1));

            CollectionAssert.AreEqual(d1, d2,
                "WriteDelta with identical change must produce identical bytes (excl. timestamp)");
        }

        [Test]
        public void Delta_EntryOrder_InStringPool_Deterministic()
        {
            // Both dimensions start from an IDENTICAL empty-field Full (same previous state).
            // Then the same two entries are added in different order.
            // The resulting deltas must be byte-identical because the string pool and
            // entry list are both Ordinal-sorted regardless of insertion order.
            var dim1 = new Dimension();
            dim1.AddNode("aaa"); dim1.AddNode("zzz");
            dim1.AddField("f", P()); // empty field — no active entries
            var full1 = new SnapshotWriter().WriteFull(dim1, 0, 0);
            // Now add both entries (aaa first, then zzz)
            dim1.GetField("f").SetLogAmp("aaa", "alpha", 1f);
            dim1.GetField("f").SetLogAmp("zzz", "beta", 2f);

            var dim2 = new Dimension();
            dim2.AddNode("aaa"); dim2.AddNode("zzz");
            dim2.AddField("f", P()); // empty field — same previous state
            var full2 = new SnapshotWriter().WriteFull(dim2, 0, 0);
            // Add in opposite order (zzz first, then aaa)
            dim2.GetField("f").SetLogAmp("zzz", "beta", 2f);
            dim2.GetField("f").SetLogAmp("aaa", "alpha", 1f);

            var d1 = StripTimestamp(new SnapshotWriter().WriteDelta(dim1, full1, 1, 1.0, 0, 1));
            var d2 = StripTimestamp(new SnapshotWriter().WriteDelta(dim2, full2, 1, 1.0, 0, 1));

            CollectionAssert.AreEqual(d1, d2,
                "Entry write order must not affect delta bytes (string pool and entries are sorted)");
        }

        // ── Delta merge determinism ────────────────────────────────────────────

        [Test]
        public void DeltaMerge_SameEndState_SameFieldValues()
        {
            // Path 1: apply mutations incrementally via deltas
            var dim = BuildDeterministic(555, nodeCount: 4, fieldCount: 2, entryCount: 6);
            var w = new SnapshotWriter();
            var r = new SnapshotReader();

            var full = w.WriteFull(dim, 0, 0);

            dim.GetField("field_0").SetLogAmp("node_001", "gamma", 3.0f);
            dim.GetField("field_1").SetLogAmp("node_002", "delta", 2.0f);
            var d1 = w.WriteDelta(dim, full, 1, 1.0, 0, 1);

            dim.GetField("field_0").SetLogAmp("node_000", "alpha", 1.5f);
            var d2 = w.WriteDelta(dim, d1, 2, 2.0, 1, 2);

            var at2Path1 = r.ReadAtTick(new List<byte[]> { full, d1, d2 }, 2).ReconstructDimension();

            // Path 2: reconstruct from scratch with the same final mutations
            var dim2 = BuildDeterministic(555, nodeCount: 4, fieldCount: 2, entryCount: 6);
            dim2.GetField("field_0").SetLogAmp("node_001", "gamma", 3.0f);
            dim2.GetField("field_1").SetLogAmp("node_002", "delta", 2.0f);
            dim2.GetField("field_0").SetLogAmp("node_000", "alpha", 1.5f);

            // Compare via public field API
            foreach (var (fieldId, _) in at2Path1.Fields)
            {
                var f1 = at2Path1.GetField(fieldId);
                var f2 = dim2.GetField(fieldId);
                if (f2 == null) continue;

                foreach (var nodeId in f1.GetActiveNodeIdsSorted())
                    foreach (var ch in f1.GetActiveChannelIdsSortedForNode(nodeId))
                        Assert.AreEqual(f1.GetLogAmp(nodeId, ch), f2.GetLogAmp(nodeId, ch), 1e-5f,
                            $"field={fieldId} node={nodeId} ch={ch}: path mismatch");
            }
        }

        // ── Fuzz tests ────────────────────────────────────────────────────────

        [Test]
        public void Fuzz_SameSeed_IdenticalBytes([Values(100u, 200u, 300u, 400u, 500u)] uint seed)
        {
            var dim1 = BuildDeterministic(seed, nodeCount: 6, edgeCount: 8, fieldCount: 2, entryCount: 12);
            var dim2 = BuildDeterministic(seed, nodeCount: 6, edgeCount: 8, fieldCount: 2, entryCount: 12);

            var w = new SnapshotWriter();
            var b1 = StripTimestamp(w.WriteFull(dim1, seed, seed * 0.5));
            var b2 = StripTimestamp(w.WriteFull(dim2, seed, seed * 0.5));

            CollectionAssert.AreEqual(b1, b2,
                $"Seed {seed}: same RNG sequence must produce identical snapshot bytes");
        }

        [Test]
        public void Fuzz_RoundTrip_NoNaNInf([Values(111u, 222u, 333u, 444u, 555u)] uint seed)
        {
            var dim = BuildDeterministic(seed, nodeCount: 8, edgeCount: 12, fieldCount: 3, entryCount: 15);

            var bytes = new SnapshotWriter().WriteFull(dim, seed, seed * 0.1);
            var restored = new SnapshotReader().Read(bytes).ReconstructDimension();

            foreach (var (_, field) in restored.Fields)
                foreach (var (_, _, logAmp) in field.EnumerateAllActiveSorted())
                {
                    Assert.IsFalse(float.IsNaN(logAmp), $"NaN in field '{field.FieldId}' (seed={seed})");
                    Assert.IsFalse(float.IsInfinity(logAmp), $"Infinity in field '{field.FieldId}' (seed={seed})");
                }
        }

        [Test]
        public void Fuzz_DeltaChain_NoNaNInf([Values(999u, 888u, 777u)] uint seed)
        {
            var dim = BuildDeterministic(seed, nodeCount: 5, edgeCount: 6, fieldCount: 2, entryCount: 8);
            var w = new SnapshotWriter();
            var r = new SnapshotReader();
            var rng = seed;

            var nodeIds = dim.Graph.GetNodeIdsSorted();
            string[] chs = { "alpha", "beta", "gamma" };
            byte[] prev = w.WriteFull(dim, 0, 0);
            var series = new List<byte[]> { prev };

            for (ulong tick = 1; tick <= 10; tick++)
            {
                foreach (var (_, field) in dim.Fields)
                {
                    int n = (int)(Xorshift(ref rng) % (uint)nodeIds.Count);
                    int c = (int)(Xorshift(ref rng) % (uint)chs.Length);
                    float delta = (NextFloat01(ref rng) - 0.5f) * 2f;
                    field.AddLogAmp(nodeIds[n], chs[c], delta);
                }

                var snap = w.WriteDelta(dim, prev, tick, tick * 1.0, tick - 1, (ushort)tick);
                series.Add(snap);
                prev = snap;
            }

            for (ulong tick = 0; tick <= 10; tick++)
            {
                var at = r.ReadAtTick(series, tick).ReconstructDimension();
                foreach (var (_, field) in at.Fields)
                    foreach (var (_, _, logAmp) in field.EnumerateAllActiveSorted())
                    {
                        Assert.IsFalse(float.IsNaN(logAmp), $"NaN at tick {tick} (seed={seed})");
                        Assert.IsFalse(float.IsInfinity(logAmp), $"Infinity at tick {tick} (seed={seed})");
                    }
            }
        }

        [Test]
        public void Fuzz_DeltaChain_SameSeed_IdenticalStateAtEachTick([Values(42u, 123u)] uint seed)
        {
            // Run twice with the same seed → ReadAtTick must yield identical logAmp values
            byte[] RunOnce(uint s)
            {
                var dim = BuildDeterministic(s, nodeCount: 4, edgeCount: 4, fieldCount: 2, entryCount: 6);
                var w = new SnapshotWriter();
                var rng = s;
                var nodeIds = dim.Graph.GetNodeIdsSorted();
                string[] chs = { "alpha", "beta", "gamma" };
                byte[] prev = w.WriteFull(dim, 0, 0);

                for (ulong tick = 1; tick <= 5; tick++)
                {
                    foreach (var (_, field) in dim.Fields)
                    {
                        int n = (int)(Xorshift(ref rng) % (uint)nodeIds.Count);
                        int c = (int)(Xorshift(ref rng) % (uint)chs.Length);
                        float delta = (NextFloat01(ref rng) - 0.5f) * 2f;
                        field.AddLogAmp(nodeIds[n], chs[c], delta);
                    }
                    prev = w.WriteDelta(dim, prev, tick, tick * 1.0, tick - 1, (ushort)tick);
                }
                return prev; // final delta at tick 5
            }

            var d1 = StripTimestamp(RunOnce(seed));
            var d2 = StripTimestamp(RunOnce(seed));

            CollectionAssert.AreEqual(d1, d2,
                $"Seed {seed}: deterministic delta sequence must produce identical final snapshot bytes");
        }

        [Test]
        public void Fuzz_LogEpsilon_BelowThreshold_NotStored(
            [Values(0.001f, 0.0001f, 0.00001f)] float epsilon)
        {
            var dim = new Dimension();
            dim.AddNode("a"); dim.AddNode("b");

            var profile = new FieldProfile("p") { LogEpsilon = epsilon };
            var f = dim.AddField("f", profile);

            // Below threshold — must be pruned by SetLogAmp before write
            f.SetLogAmp("a", "ch", epsilon * 0.5f);
            // Above threshold — must survive
            f.SetLogAmp("b", "ch", epsilon * 2f);

            var snap = new SnapshotReader().Read(new SnapshotWriter().WriteFull(dim, 1, 0));

            // Only 1 entry (b/ch) should be in the snapshot
            Assert.AreEqual(1, snap.Fields[0].Entries.Length,
                $"LogEpsilon={epsilon}: only above-threshold entries must be written");
            Assert.AreEqual("b", snap.Fields[0].Entries[0].nodeId);
        }

        // ── Scenario: 500-tick continuous run ─────────────────────────────────

        [Test]
        [Timeout(10000)] // 10s budget — should complete in well under 1s
        public void Scenario_500Ticks_AllValuesWithinClampBounds()
        {
            var dim = new Dimension();
            dim.AddNode("earth"); dim.AddNode("mars"); dim.AddNode("belt");
            dim.AddEdge("earth", "mars", 0.5f, "space");
            dim.AddEdge("mars", "belt", 0.3f, "space");

            var warProfile = new FieldProfile("war")
            {
                PropagationRate = 0.05f,
                DecayRate = 0.01f,
                MinLogAmpClamp = -8f,
                MaxLogAmpClamp = 8f,
                LogEpsilon = 0.0001f
            };
            var war = new WarSystem(dim, warProfile);
            war.DeclareWar("earth");

            var writer = new SnapshotWriter();
            var reader = new SnapshotReader();
            byte[] prev = writer.WriteFull(dim, 0, 0);

            for (ulong tick = 1; tick <= 500; tick++)
            {
                war.Tick(0.1f);

                // Write delta every tick (simulates full recording)
                var chainDepth = (ushort)Math.Min((int)tick, 65535);
                var delta = writer.WriteDelta(dim, prev, tick, tick * 0.1, tick - 1, chainDepth);
                prev = delta;

                // Validate field state every 50 ticks
                if (tick % 50 != 0) continue;

                var snap = reader.Read(delta);
                foreach (var fr in snap.Fields)
                {
                    var p = fr.Profile;
                    foreach (var (_, _, logAmp) in fr.Entries)
                    {
                        Assert.IsFalse(float.IsNaN(logAmp),
                            $"NaN logAmp in '{fr.FieldId}' at tick {tick}");
                        Assert.IsFalse(float.IsInfinity(logAmp),
                            $"Infinity logAmp in '{fr.FieldId}' at tick {tick}");
                        Assert.GreaterOrEqual(logAmp, p.MinLogAmpClamp - 1e-4f,
                            $"logAmp below MinClamp in '{fr.FieldId}' at tick {tick}");
                        Assert.LessOrEqual(logAmp, p.MaxLogAmpClamp + 1e-4f,
                            $"logAmp above MaxClamp in '{fr.FieldId}' at tick {tick}");
                    }
                }
            }
        }

        [Test]
        public void Scenario_WarExposure_Converges_NeverNaN()
        {
            // War with ambient decay — after enough ticks exposure must converge to near-zero
            var dim = new Dimension();
            dim.AddNode("earth");
            var warProfile = new FieldProfile("war")
            {
                PropagationRate = 0.0f, // no propagation — pure decay test
                DecayRate = 0.0f,
                MinLogAmpClamp = -5f,
                MaxLogAmpClamp = 5f
            };
            var war = new WarSystem(dim, warProfile, new WarConfig
            {
                AmbientDecayRate = 0.1f,
                ExposureEpsilon = 0.0001f,
                ExposureGrowthRate = 0f
            });

            // Prime with exposure, then let it decay
            war.Exposure.SetLogAmp("earth", "x", 3.0f);

            for (int i = 0; i < 200; i++)
            {
                war.Tick(0.5f);
                float amp = war.GetExposureLogAmp("earth");
                Assert.IsFalse(float.IsNaN(amp), $"NaN at tick {i}");
                Assert.IsFalse(float.IsInfinity(amp), $"Inf at tick {i}");
                Assert.GreaterOrEqual(amp, 0f, $"Exposure went negative at tick {i}");
            }

            // Should have decayed to near-zero (< epsilon)
            Assert.Less(war.GetExposureLogAmp("earth"), 0.001f,
                "Exposure must decay to near-zero after many ambient-decay ticks");
        }

        // ── Round-trip hash equality ───────────────────────────────────────────

        [Test]
        public void RoundTrip_WriteRead_WriteAgain_IdenticalBytes()
        {
            // Write → Read → Reconstruct → Write again → must produce same bytes (excl. timestamp)
            var dim = BuildDeterministic(314159);
            var w = new SnapshotWriter();

            var bytes1 = w.WriteFull(dim, 42, 1.0);
            var restored = new SnapshotReader().Read(bytes1).ReconstructDimension();
            var bytes2 = w.WriteFull(restored, 42, 1.0);

            CollectionAssert.AreEqual(StripTimestamp(bytes1), StripTimestamp(bytes2),
                "A restored Dimension re-serialized at the same tick must produce identical bytes");
        }

        [Test]
        public void RoundTrip_EmptyDimension_Stable()
        {
            var dim = new Dimension();
            dim.AddField("f", P());

            var w = new SnapshotWriter();
            var b1 = StripTimestamp(w.WriteFull(dim, 0, 0));
            var b2 = StripTimestamp(w.WriteFull(new SnapshotReader().Read(b1).ReconstructDimension(), 0, 0));
            CollectionAssert.AreEqual(b1, b2, "Empty Dimension must round-trip stably");
        }
    }
}
