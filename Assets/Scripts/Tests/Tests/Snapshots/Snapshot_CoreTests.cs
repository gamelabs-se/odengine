using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;
using Odengine.Serialization;

namespace Odengine.Tests.Snapshots
{
    /// <summary>
    /// Tier 1 — Pure serialization layer: header, graph, field, delta, validation, DeltaIndex.
    /// All tests construct Dimension directly. No domain systems involved.
    /// </summary>
    [TestFixture]
    public class Snapshot_CoreTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static Dimension TwoNodeDim()
        {
            var d = new Dimension();
            d.AddNode("earth", "Earth");
            d.AddNode("mars", "Mars");
            d.AddEdge("earth", "mars", 0.5f, "space");
            return d;
        }

        private static FieldProfile P(string id = "test") =>
            new FieldProfile(id) { PropagationRate = 0.2f, DecayRate = 0.01f, LogEpsilon = 0.0001f };

        private static SnapshotWriter W() => new SnapshotWriter();
        private static SnapshotReader R() => new SnapshotReader();

        /// <summary>Zero the 8-byte created_utc_ms timestamp at header offset 23 so byte comparisons ignore wall-clock time.</summary>
        private static byte[] StripTimestamp(byte[] bytes)
        {
            var copy = (byte[])bytes.Clone();
            // Header: magic(4) + schema_version(2) + type(1) + tick(8) + sim_time(8) = 23 → timestamp starts at 23
            for (int i = 23; i < 31; i++) copy[i] = 0;
            return copy;
        }

        // ── Header ────────────────────────────────────────────────────────────

        [Test]
        public void Header_SnapshotType_Full()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var snap = R().Read(W().WriteFull(dim, 42, 3.14));
            Assert.AreEqual(SnapshotType.Full, snap.Header.SnapshotType);
        }

        [Test]
        public void Header_Tick_RoundTrips()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var snap = R().Read(W().WriteFull(dim, 99999UL, 0.0));
            Assert.AreEqual(99999UL, snap.Header.Tick);
        }

        [Test]
        public void Header_SimTime_RoundTrips()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var snap = R().Read(W().WriteFull(dim, 1, 12345.6789));
            Assert.AreEqual(12345.6789, snap.Header.SimTime, 1e-6);
        }

        [Test]
        public void Header_EngineVersion_RoundTrips()
        {
            var config = new SnapshotConfig { EngineVersion = "1.2.3-beta" };
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var snap = R().Read(new SnapshotWriter(config).WriteFull(dim, 1, 0));
            Assert.AreEqual("1.2.3-beta", snap.Header.EngineVersion);
        }

        [Test]
        public void Header_Checkpoint_TypeIsCheckpoint()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var snap = R().Read(W().WriteCheckpoint(dim, 7, 0, new List<ISnapshotParticipant>()));
            Assert.AreEqual(SnapshotType.Checkpoint, snap.Header.SnapshotType);
        }

        [Test]
        public void Header_Delta_TypeIsDelta()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P()).SetLogAmp("earth", "x", 1f);
            var full = W().WriteFull(dim, 0, 0);
            dim.GetField("f").SetLogAmp("earth", "x", 2f);
            var snap = R().Read(W().WriteDelta(dim, full, 1, 1.0, 0, 1));
            Assert.AreEqual(SnapshotType.Delta, snap.Header.SnapshotType);
        }

        [Test]
        public void Header_ParentTick_RoundTrips()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P()).SetLogAmp("earth", "x", 1f);
            var full = W().WriteFull(dim, 0, 0);
            var snap = R().Read(W().WriteDelta(dim, full, 5, 5.0, parentTick: 0, chainDepth: 2));
            Assert.AreEqual(0UL, snap.Header.ParentTick);
            Assert.AreEqual((ushort)2, snap.Header.DeltaChainDepth);
        }

        // ── Graph ─────────────────────────────────────────────────────────────

        [Test]
        public void Graph_Nodes_IdAndName_RoundTrip()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            Assert.IsNotNull(restored.Graph.Nodes["earth"]);
            Assert.IsNotNull(restored.Graph.Nodes["mars"]);
            Assert.AreEqual("Earth", restored.Graph.Nodes["earth"].Name);
            Assert.AreEqual("Mars",  restored.Graph.Nodes["mars"].Name);
        }

        [Test]
        public void Graph_Edges_ResistanceAndEndpoints_RoundTrip()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            var edges = restored.Graph.GetOutEdgesSorted("earth");
            Assert.AreEqual(1, edges.Count);
            Assert.AreEqual("mars", edges[0].ToId);
            Assert.AreEqual(0.5f,   edges[0].Resistance, 1e-6f);
        }

        [Test]
        public void Graph_EdgeTags_SingleTag_RoundTrip()
        {
            var dim = TwoNodeDim(); // earth→mars tagged "space"
            dim.AddField("f", P());
            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            var edges = restored.Graph.GetOutEdgesSorted("earth");
            Assert.AreEqual(1, edges.Count);
            CollectionAssert.Contains(edges[0].Tags, "space");
        }

        [Test]
        public void Graph_EdgeTags_MultipleTags_RoundTrip()
        {
            var dim = new Dimension();
            dim.AddNode("a"); dim.AddNode("b");
            dim.AddEdge("a", "b", 1f, "sea", "trade", "regulated");
            dim.AddField("f", P());
            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            var edges = restored.Graph.GetOutEdgesSorted("a");
            CollectionAssert.AreEquivalent(new[] { "sea", "trade", "regulated" }, edges[0].Tags);
        }

        [Test]
        public void Graph_MultipleEdgesPerNode_AllPreserved()
        {
            var dim = new Dimension();
            dim.AddNode("hub"); dim.AddNode("a"); dim.AddNode("b"); dim.AddNode("c");
            dim.AddEdge("hub", "a", 0.1f);
            dim.AddEdge("hub", "b", 0.2f);
            dim.AddEdge("hub", "c", 0.3f);
            dim.AddField("f", P());
            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            var edges = restored.Graph.GetOutEdgesSorted("hub");
            Assert.AreEqual(3, edges.Count);
        }

        [Test]
        public void Graph_EmptyDimension_ZeroNodes()
        {
            var dim = new Dimension();
            dim.AddField("f", P());
            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            Assert.AreEqual(0, restored.Graph.Nodes.Count);
        }

        [Test]
        public void Graph_Delta_DefaultOmitsGraph()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P()).SetLogAmp("earth", "x", 1f);
            var full = W().WriteFull(dim, 0, 0);
            dim.GetField("f").SetLogAmp("earth", "x", 2f);

            // Delta without AlwaysIncludeGraph → Nodes/Edges should be empty in the delta record
            var delta = W().WriteDelta(dim, full, 1, 1.0, 0, 1);
            var deltaSnap = R().Read(delta);
            Assert.AreEqual(0, deltaSnap.Nodes.Length, "Delta should omit graph by default");
        }

        [Test]
        public void Graph_Delta_AlwaysIncludeGraph_WritesGraph()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P()).SetLogAmp("earth", "x", 1f);
            var full = W().WriteFull(dim, 0, 0);
            dim.GetField("f").SetLogAmp("earth", "x", 2f);

            var config = new SnapshotConfig { AlwaysIncludeGraph = true };
            var delta = new SnapshotWriter(config).WriteDelta(dim, full, 1, 1.0, 0, 1);
            var deltaSnap = R().Read(delta);
            Assert.AreEqual(2, deltaSnap.Nodes.Length, "Delta with AlwaysIncludeGraph must include nodes");
        }

        // ── Fields ────────────────────────────────────────────────────────────

        [Test]
        public void Field_LogAmp_ExactRoundTrip()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("economy.price", P("econ"));
            f.SetLogAmp("earth", "water",  1.5f);
            f.SetLogAmp("mars",  "ore",   -0.7f);

            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            var rf = restored.GetField("economy.price");
            Assert.AreEqual( 1.5f, rf.GetLogAmp("earth", "water"), 1e-6f);
            Assert.AreEqual(-0.7f, rf.GetLogAmp("mars",  "ore"),   1e-6f);
        }

        [Test]
        public void Field_NeutralEntries_NotWritten_NotRestored()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "water", 0.5f);
            // mars/water stays at neutral — must not appear after restoration

            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            var rf = restored.GetField("f");

            Assert.AreEqual(0.5f, rf.GetLogAmp("earth", "water"), 1e-6f, "Active entry preserved");
            Assert.AreEqual(0,    rf.GetActiveChannelIdsSortedForNode("mars").Count,
                "Neutral mars node must have no active channels");
        }

        [Test]
        public void Field_NeutralEntries_NotWritten_EntryCountInSnapshot()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "water", 0.5f);
            // Only 1 active entry

            var snap = R().Read(W().WriteFull(dim, 1, 0));
            Assert.AreEqual(1, snap.Fields[0].Entries.Length,
                "Only above-epsilon entries must be written to snapshot");
        }

        [Test]
        public void Field_Profile_AllSixValuesRoundTrip()
        {
            var profile = new FieldProfile("custom")
            {
                PropagationRate     = 0.33f,
                EdgeResistanceScale = 2.5f,
                DecayRate           = 0.007f,
                MinLogAmpClamp      = -15f,
                MaxLogAmpClamp      = 18f,
                LogEpsilon          = 0.0005f
            };
            var dim = TwoNodeDim();
            dim.AddField("f", profile).SetLogAmp("earth", "signal", 1.0f);

            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            var rp = restored.GetField("f").Profile;

            Assert.AreEqual("custom",  rp.ProfileId);
            Assert.AreEqual(0.33f,     rp.PropagationRate,     1e-6f);
            Assert.AreEqual(2.5f,      rp.EdgeResistanceScale, 1e-6f);
            Assert.AreEqual(0.007f,    rp.DecayRate,           1e-6f);
            Assert.AreEqual(-15f,      rp.MinLogAmpClamp,      1e-6f);
            Assert.AreEqual(18f,       rp.MaxLogAmpClamp,      1e-6f);
            Assert.AreEqual(0.0005f,   rp.LogEpsilon,          1e-7f);
        }

        [Test]
        public void Field_LogEpsilon_SameAsProfile()
        {
            // LogEpsilon moved from private const to FieldProfile — verify it round-trips and is honoured.
            var dim = TwoNodeDim();
            var profile = new FieldProfile("p") { LogEpsilon = 0.0002f };
            var f = dim.AddField("f", profile);
            f.SetLogAmp("earth", "x", 0.00015f); // below epsilon → pruned immediately
            f.SetLogAmp("mars",  "y", 0.00025f); // above epsilon → stored

            var snap = R().Read(W().WriteFull(dim, 1, 0));
            // Only mars/y survives
            Assert.AreEqual(1, snap.Fields[0].Entries.Length);
            Assert.AreEqual("mars", snap.Fields[0].Entries[0].nodeId);
        }

        [Test]
        public void Field_MultipleFields_AllRoundTrip()
        {
            var dim = TwoNodeDim();
            dim.AddField("war.exposure",    P("war"))    .SetLogAmp("earth", "x",         2.0f);
            dim.AddField("faction.presence", P("faction")).SetLogAmp("mars",  "empire_a",  1.5f);

            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            Assert.AreEqual(2.0f, restored.GetField("war.exposure")    .GetLogAmp("earth", "x"),        1e-6f);
            Assert.AreEqual(1.5f, restored.GetField("faction.presence").GetLogAmp("mars",  "empire_a"), 1e-6f);
        }

        [Test]
        public void Field_EmptyField_ZeroEntries_StillRegistered()
        {
            var dim = TwoNodeDim();
            dim.AddField("empty", P());

            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            Assert.IsNotNull(restored.GetField("empty"), "Empty field must be registered in restored Dimension");
            Assert.AreEqual(0f, restored.GetField("empty").GetLogAmp("earth", "x"), 1e-9f);
        }

        [Test]
        public void Field_ManyChannels_AllRoundTrip()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("trade", P());
            string[] items = { "food", "medicine", "ore", "rifles", "water" };
            foreach (var item in items)
                f.SetLogAmp("earth", item, 1.0f + item.Length * 0.1f);

            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            var rf = restored.GetField("trade");
            foreach (var item in items)
                Assert.AreEqual(1.0f + item.Length * 0.1f, rf.GetLogAmp("earth", item), 1e-5f,
                    $"Channel '{item}' logAmp mismatch");
        }

        [Test]
        public void Field_GetFieldIds_OrderedOrdinally()
        {
            var dim = TwoNodeDim();
            dim.AddField("zzz.field", P("z")).SetLogAmp("earth", "x", 0.5f);
            dim.AddField("aaa.field", P("a")).SetLogAmp("earth", "x", 0.5f);
            dim.AddField("mmm.field", P("m")).SetLogAmp("earth", "x", 0.5f);

            var snap = R().Read(W().WriteFull(dim, 1, 0));
            var ids = snap.GetFieldIds();

            CollectionAssert.IsOrdered(ids, StringComparer.Ordinal,
                "GetFieldIds() must return Ordinal-sorted IDs");
            CollectionAssert.AreEquivalent(new[] { "aaa.field", "mmm.field", "zzz.field" }, ids);
        }

        [Test]
        public void Field_NegativeLogAmp_RoundTrips()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "drain", -3.5f);

            var restored = R().Read(W().WriteFull(dim, 1, 0)).ReconstructDimension();
            Assert.AreEqual(-3.5f, restored.GetField("f").GetLogAmp("earth", "drain"), 1e-6f);
        }

        // ── Delta ─────────────────────────────────────────────────────────────

        [Test]
        public void Delta_OnlyChangedEntry_InDeltaRecord()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "water", 1.0f);
            f.SetLogAmp("mars",  "ore",   2.0f);
            var full = W().WriteFull(dim, 0, 0);

            // Only change earth/water; mars/ore is unchanged
            f.SetLogAmp("earth", "water", 1.5f);

            var snap = R().Read(W().WriteDelta(dim, full, 1, 1.0, 0, 1));
            Assert.AreEqual(1, snap.Fields[0].Entries.Length,
                "Only the changed entry should appear in the delta record");
            Assert.AreEqual("earth", snap.Fields[0].Entries[0].nodeId);
            Assert.AreEqual("water", snap.Fields[0].Entries[0].channelId);
            Assert.AreEqual(1.5f,    snap.Fields[0].Entries[0].logAmp, 1e-6f);
        }

        [Test]
        public void Delta_RemovedEntry_WrittenAsSentinelZero()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "water", 1.0f);
            var full = W().WriteFull(dim, 0, 0);

            f.SetLogAmp("earth", "water", 0f); // removes entry (neutral)

            var snap = R().Read(W().WriteDelta(dim, full, 1, 1.0, 0, 1));
            Assert.AreEqual(1, snap.Fields[0].Entries.Length,
                "Removed entry must appear as sentinel in delta");
            Assert.AreEqual(0f, snap.Fields[0].Entries[0].logAmp, 1e-9f,
                "Removed entry must have logAmp=0 sentinel");
        }

        [Test]
        public void Delta_NoChange_ZeroEntries()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "water", 1.0f);
            var full = W().WriteFull(dim, 0, 0);
            // No mutation

            var snap = R().Read(W().WriteDelta(dim, full, 1, 1.0, 0, 1));
            Assert.AreEqual(0, snap.Fields[0].Entries.Length,
                "Unchanged state should produce an empty delta entry list");
        }

        [Test]
        public void Delta_ReadAtTick_ReconstructsBothChangedAndUnchanged()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "water", 1.0f);
            f.SetLogAmp("mars",  "ore",   2.0f);
            var full = W().WriteFull(dim, 0, 0);

            f.SetLogAmp("earth", "water", 1.5f); // changed
            // mars/ore unchanged at 2.0f

            var delta = W().WriteDelta(dim, full, 1, 1.0, 0, 1);
            var at1 = R().ReadAtTick(new List<byte[]> { full, delta }, 1);
            var dim1 = at1.ReconstructDimension();

            Assert.AreEqual(1.5f, dim1.GetField("f").GetLogAmp("earth", "water"), 1e-5f,
                "Changed entry must be at new value");
            Assert.AreEqual(2.0f, dim1.GetField("f").GetLogAmp("mars",  "ore"),   1e-5f,
                "Unchanged entry must be preserved from base");
        }

        [Test]
        public void Delta_ReadAtTick_RemovalMergedCorrectly()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "water", 1.0f);
            f.SetLogAmp("mars",  "ore",   2.0f);
            var full = W().WriteFull(dim, 0, 0);

            f.SetLogAmp("mars", "ore", 0f); // remove
            var delta = W().WriteDelta(dim, full, 1, 1.0, 0, 1);

            var at1 = R().ReadAtTick(new List<byte[]> { full, delta }, 1).ReconstructDimension();
            Assert.AreEqual(0f, at1.GetField("f").GetLogAmp("mars", "ore"), 1e-9f,
                "Sentinel removal must be applied by merge");
            Assert.AreEqual(0, at1.GetField("f").GetActiveChannelIdsSortedForNode("mars").Count,
                "Removed entry must not appear as active after merge");
        }

        [Test]
        public void Delta_ChainOf3_CorrectAtEachTick()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "pressure", 1.0f);
            var full = W().WriteFull(dim, 0, 0);
            var series = new List<byte[]> { full };
            byte[] prev = full;

            // Tick 1: pressure → 2.0, add mars/signal 0.5
            f.SetLogAmp("earth", "pressure", 2.0f);
            f.SetLogAmp("mars",  "signal",   0.5f);
            var d1 = W().WriteDelta(dim, prev, 1, 1.0, 0, 1);
            series.Add(d1); prev = d1;

            // Tick 2: pressure → 3.0, signal removed
            f.SetLogAmp("earth", "pressure", 3.0f);
            f.SetLogAmp("mars",  "signal",   0f);
            var d2 = W().WriteDelta(dim, prev, 2, 2.0, 1, 2);
            series.Add(d2); prev = d2;

            // Tick 3: pressure → 4.0
            f.SetLogAmp("earth", "pressure", 4.0f);
            var d3 = W().WriteDelta(dim, prev, 3, 3.0, 2, 3);
            series.Add(d3);

            var reader = R();

            var at1 = reader.ReadAtTick(series, 1).ReconstructDimension();
            Assert.AreEqual(2.0f, at1.GetField("f").GetLogAmp("earth", "pressure"), 1e-5f);
            Assert.AreEqual(0.5f, at1.GetField("f").GetLogAmp("mars",  "signal"),   1e-5f);

            var at2 = reader.ReadAtTick(series, 2).ReconstructDimension();
            Assert.AreEqual(3.0f, at2.GetField("f").GetLogAmp("earth", "pressure"), 1e-5f);
            Assert.AreEqual(0f,   at2.GetField("f").GetLogAmp("mars",  "signal"),   1e-9f);

            var at3 = reader.ReadAtTick(series, 3).ReconstructDimension();
            Assert.AreEqual(4.0f, at3.GetField("f").GetLogAmp("earth", "pressure"), 1e-5f);
        }

        [Test]
        public void Delta_ReadAtExactFullTick_ReturnsFull()
        {
            var dim = TwoNodeDim();
            var f = dim.AddField("f", P());
            f.SetLogAmp("earth", "x", 1.0f);
            var full = W().WriteFull(dim, 5, 5.0);

            f.SetLogAmp("earth", "x", 2.0f);
            var delta = W().WriteDelta(dim, full, 6, 6.0, 5, 1);

            var at5 = R().ReadAtTick(new List<byte[]> { full, delta }, 5).ReconstructDimension();
            Assert.AreEqual(1.0f, at5.GetField("f").GetLogAmp("earth", "x"), 1e-6f,
                "ReadAtTick at the exact Full tick should return the Full state");
        }

        // ── Validation ────────────────────────────────────────────────────────

        [Test]
        public void Validation_BadMagic_Throws()
        {
            var bytes = new byte[40]; // all zeros → magic 0x00000000
            Assert.Throws<InvalidSnapshotException>(() => R().Read(bytes));
        }

        [Test]
        public void Validation_TooShort_Throws()
        {
            Assert.Throws<InvalidSnapshotException>(() => R().Read(new byte[3]));
        }

        [Test]
        public void Validation_NewerSchemaVersion_Throws()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var bytes = W().WriteFull(dim, 1, 0);
            // Patch schema version at bytes [4..5] to 0xFFFF
            bytes[4] = 0xFF; bytes[5] = 0xFF;
            Assert.Throws<SnapshotVersionException>(() => R().Read(bytes));
        }

        [Test]
        public void Validation_NullBytes_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => R().Read(null));
        }

        [Test]
        public void Validation_NullDimension_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => W().WriteFull(null, 1, 0));
        }

        [Test]
        public void Validation_ReadAtTick_EmptySeries_Throws()
        {
            Assert.Throws<MissingParentSnapshotException>(() =>
                R().ReadAtTick(new List<byte[]>(), 1));
        }

        [Test]
        public void Validation_ReadAtTick_NoFullBeforeTarget_Throws()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P()).SetLogAmp("earth", "x", 1f);
            // Full is at tick 10, asking for tick 5 → no base found
            var full = W().WriteFull(dim, 10, 10.0);
            Assert.Throws<MissingParentSnapshotException>(() =>
                R().ReadAtTick(new List<byte[]> { full }, 5));
        }

        [Test]
        public void Validation_NaNLogAmpCheck_PassesOnCleanData()
        {
            var config = new SnapshotConfig { ValidateLogAmpSanityOnWrite = true };
            var dim = TwoNodeDim();
            dim.AddField("f", P()).SetLogAmp("earth", "x", 5.0f);
            Assert.DoesNotThrow(() => new SnapshotWriter(config).WriteFull(dim, 1, 0),
                "Validator must not throw on clean (non-NaN) data");
        }

        // ── DeltaIndex ────────────────────────────────────────────────────────

        [Test]
        public void DeltaIndex_Append_IncreasesCount()
        {
            var idx = new DeltaIndex();
            idx.Append(0, SnapshotType.Full,  0, 500);
            idx.Append(1, SnapshotType.Delta, 500,  20);
            idx.Append(2, SnapshotType.Delta, 520,  15);
            Assert.AreEqual(3, idx.Count);
        }

        [Test]
        public void DeltaIndex_LastTick_EmptyIsZero()
        {
            Assert.AreEqual(0UL, new DeltaIndex().LastTick);
        }

        [Test]
        public void DeltaIndex_LastTick_ReturnsLastAppended()
        {
            var idx = new DeltaIndex();
            idx.Append(5,  SnapshotType.Full,  0, 100);
            idx.Append(10, SnapshotType.Delta, 100, 20);
            Assert.AreEqual(10UL, idx.LastTick);
        }

        [Test]
        public void DeltaIndex_FindFullBefore_ReturnsLastFullAtOrBefore()
        {
            var idx = new DeltaIndex();
            idx.Append(0,  SnapshotType.Full,    0,   500);
            idx.Append(1,  SnapshotType.Delta, 500,    20);
            idx.Append(5,  SnapshotType.Full,  520,   400);
            idx.Append(6,  SnapshotType.Delta, 920,    18);
            idx.Append(10, SnapshotType.Delta, 938,    22);

            // At tick 4 → should find the Full at tick 0 (tick 5 Full is after 4)
            var r0 = idx.FindFullBefore(4);
            Assert.IsNotNull(r0);
            Assert.AreEqual(0L, r0.Value.offset, "Should pick Full at tick 0 when querying tick 4");

            // At tick 7 → should find Full at tick 5
            var r5 = idx.FindFullBefore(7);
            Assert.IsNotNull(r5);
            Assert.AreEqual(520L, r5.Value.offset, "Should pick Full at tick 5 when querying tick 7");

            // At tick 5 exactly → Full at tick 5
            var r5exact = idx.FindFullBefore(5);
            Assert.IsNotNull(r5exact);
            Assert.AreEqual(520L, r5exact.Value.offset);
        }

        [Test]
        public void DeltaIndex_FindFullBefore_EmptyIndex_Null()
        {
            Assert.IsNull(new DeltaIndex().FindFullBefore(100));
        }

        [Test]
        public void DeltaIndex_FindFullBefore_NoFullBeforeTick_Null()
        {
            var idx = new DeltaIndex();
            idx.Append(10, SnapshotType.Full, 0, 500);
            Assert.IsNull(idx.FindFullBefore(5), "Full at tick 10 is after tick 5 → null");
        }

        [Test]
        public void DeltaIndex_FindDeltaRange_ReturnsInRange()
        {
            var idx = new DeltaIndex();
            idx.Append(0, SnapshotType.Full,    0, 500);
            idx.Append(1, SnapshotType.Delta, 500,  20);
            idx.Append(2, SnapshotType.Delta, 520,  15);
            idx.Append(3, SnapshotType.Delta, 535,  12);
            idx.Append(5, SnapshotType.Full,  547, 400);

            var deltas = idx.FindDeltaRange(afterTick: 0, upToTick: 2);
            Assert.AreEqual(2, deltas.Count);
            Assert.AreEqual(500L, deltas[0].offset);
            Assert.AreEqual(520L, deltas[1].offset);
        }

        [Test]
        public void DeltaIndex_FindDeltaRange_SkipsFullSnapshots()
        {
            var idx = new DeltaIndex();
            idx.Append(0, SnapshotType.Full,  0, 500);
            idx.Append(1, SnapshotType.Delta, 500, 20);
            idx.Append(2, SnapshotType.Full,  520, 400); // Full in the middle — must be skipped
            idx.Append(3, SnapshotType.Delta, 920,  15);

            var deltas = idx.FindDeltaRange(0, 3);
            Assert.AreEqual(2, deltas.Count, "Full snapshots must be excluded from delta range");
            Assert.AreEqual(500L, deltas[0].offset);
            Assert.AreEqual(920L, deltas[1].offset);
        }

        [Test]
        public void DeltaIndex_FindDeltaRange_NoDeltas_Empty()
        {
            var idx = new DeltaIndex();
            idx.Append(0,  SnapshotType.Full, 0, 500);
            idx.Append(10, SnapshotType.Full, 500, 400);
            Assert.AreEqual(0, idx.FindDeltaRange(0, 9).Count);
        }

        [Test]
        public void DeltaIndex_CheckpointCountsAsFullBefore()
        {
            var idx = new DeltaIndex();
            idx.Append(0,  SnapshotType.Full,       0, 500);
            idx.Append(5,  SnapshotType.Checkpoint, 500, 900);
            idx.Append(6,  SnapshotType.Delta,     1400,  20);

            var r = idx.FindFullBefore(7);
            Assert.IsNotNull(r);
            Assert.AreEqual(500L, r.Value.offset, "Checkpoint should be found by FindFullBefore");
        }

        [Test]
        public void DeltaIndex_SaveLoad_ExactRoundTrip()
        {
            var idx = new DeltaIndex();
            idx.Append(0,  SnapshotType.Full,        0, 1000);
            idx.Append(1,  SnapshotType.Delta,    1000,   50);
            idx.Append(2,  SnapshotType.Delta,    1050,   45);
            idx.Append(10, SnapshotType.Checkpoint, 1095, 900);

            byte[] indexBytes;
            using (var ms = new MemoryStream())
            {
                idx.SaveIndex(ms);
                indexBytes = ms.ToArray();
            }

            DeltaIndex restored;
            using (var ms = new MemoryStream(indexBytes))
                restored = DeltaIndex.LoadIndex(ms);

            Assert.AreEqual(4, restored.Count);
            Assert.AreEqual(10UL, restored.LastTick);

            var full = restored.FindFullBefore(3);
            Assert.IsNotNull(full);
            Assert.AreEqual(0L, full.Value.offset);

            var checkpoint = restored.FindFullBefore(10);
            Assert.IsNotNull(checkpoint);
            Assert.AreEqual(1095L, checkpoint.Value.offset);

            var deltas = restored.FindDeltaRange(0, 2);
            Assert.AreEqual(2, deltas.Count);
        }

        [Test]
        public void DeltaIndex_LargeIndex_SaveLoadRoundTrip()
        {
            var idx = new DeltaIndex();
            for (ulong i = 0; i < 1000; i++)
                idx.Append(i, i % 100 == 0 ? SnapshotType.Full : SnapshotType.Delta, (long)i * 50, 50);

            byte[] bytes;
            using (var ms = new MemoryStream()) { idx.SaveIndex(ms); bytes = ms.ToArray(); }

            DeltaIndex loaded;
            using (var ms = new MemoryStream(bytes)) loaded = DeltaIndex.LoadIndex(ms);

            Assert.AreEqual(1000, loaded.Count);
            Assert.AreEqual(999UL, loaded.LastTick);

            var r = loaded.FindFullBefore(150);
            Assert.IsNotNull(r);
            Assert.AreEqual(100L * 50, r.Value.offset);
        }
    }
}
