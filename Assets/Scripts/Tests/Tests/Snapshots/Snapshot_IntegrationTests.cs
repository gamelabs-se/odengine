using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Faction;
using Odengine.Fields;
using Odengine.Serialization;
using Odengine.War;

namespace Odengine.Tests.Snapshots
{
    /// <summary>
    /// Tier 4 — Full workflows: bootstrap → checkpoint → restore → tick without divergence.
    /// Crosses all domain systems. Tests resume-safety, occupation survival, DeltaIndex recording.
    /// </summary>
    [TestFixture]
    public class Snapshot_IntegrationTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static FieldProfile P(string id) =>
            new FieldProfile(id) { PropagationRate = 0.05f, DecayRate = 0f, LogEpsilon = 0.0001f };

        private static Dimension BuildWorld()
        {
            var dim = new Dimension();
            dim.AddNode("earth", "Earth");
            dim.AddNode("mars",  "Mars");
            dim.AddNode("belt",  "Asteroid Belt");
            dim.AddEdge("earth", "mars", 0.5f, "space");
            dim.AddEdge("mars",  "belt", 0.3f, "space");
            return dim;
        }

        private static Dimension BuildWorld2()
        {
            var dim = new Dimension();
            dim.AddNode("earth", "Earth");
            dim.AddNode("mars",  "Mars");
            dim.AddNode("belt",  "Asteroid Belt");
            dim.AddEdge("earth", "mars", 0.5f, "space");
            dim.AddEdge("mars",  "belt", 0.3f, "space");
            return dim;
        }

        // ── War resume ────────────────────────────────────────────────────────

        [Test]
        public void WarResume_FieldAndStateRestoredExactly()
        {
            // Bootstrap
            var dim = BuildWorld();
            var war = new WarSystem(dim, P("war"));
            war.DeclareWar("earth");
            war.DeclareOccupation("mars", "earth_empire");
            war.SetNodeStability("mars", 0.6f);
            war.Tick(2f);

            float progressBefore = war.GetOccupationProgress("mars");
            float exposureBefore = war.GetExposureLogAmp("earth");

            // Checkpoint
            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 10, 20.0, new ISnapshotParticipant[] { war });

            // Restore
            var dim2 = BuildWorld2();
            var war2 = new WarSystem(dim2, P("war"));
            var snap = new SnapshotReader().Read(bytes);
            snap.RestoreFields(dim2);
            snap.RestoreSystem(war2);

            Assert.IsTrue(war2.IsAtWar("earth"),               "Active war state must be restored");
            Assert.AreEqual("earth_empire", war2.GetOccupationAttacker("mars"), "Attacker must be restored");
            Assert.AreEqual(progressBefore,  war2.GetOccupationProgress("mars"), 1e-5f, "Progress must be restored");
            Assert.AreEqual(0.6f,            war2.GetNodeStability("mars"),      1e-6f, "Stability must be restored");
            Assert.AreEqual(exposureBefore,  war2.GetExposureLogAmp("earth"),   1e-5f, "Exposure logAmp must be restored");
        }

        [Test]
        public void WarResume_TickContinuesFromRestoredState()
        {
            var dim = BuildWorld();
            var war = new WarSystem(dim, P("war"));
            war.DeclareWar("earth");
            war.Tick(1f);
            float exposureAfterTick1 = war.GetExposureLogAmp("earth");

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 1.0, new ISnapshotParticipant[] { war });

            var dim2 = BuildWorld2();
            var war2 = new WarSystem(dim2, P("war"));
            new SnapshotReader().Read(bytes).RestoreFields(dim2);
            new SnapshotReader().Read(bytes).RestoreSystem(war2);

            war2.Tick(1f);
            float exposureAfterTick2 = war2.GetExposureLogAmp("earth");

            // Exposure should have grown by exactly one tick's worth after resume
            Assert.AreEqual(
                exposureAfterTick1 + new WarConfig().ExposureGrowthRate,
                exposureAfterTick2,
                0.001f,
                "Resumed war exposure must continue accumulating from saved state");
        }

        // ── Occupation completes after resume ─────────────────────────────────

        [Test]
        public void Occupation_CompletesAfterResume()
        {
            var dim = BuildWorld();
            var war = new WarSystem(dim, P("war"), new WarConfig
            {
                OccupationBaseRate        = 0.5f,
                OccupationStabilityResist = 0.0f
            });
            war.DeclareWar("mars");
            war.DeclareOccupation("mars", "earth_empire");
            war.Tick(1f); // progress = 0.5

            float progressBefore = war.GetOccupationProgress("mars");
            Assert.AreEqual(0.5f, progressBefore, 0.01f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 5, 5.0, new ISnapshotParticipant[] { war });

            var dim2 = BuildWorld2();
            var war2 = new WarSystem(dim2, P("war"));
            new SnapshotReader().Read(bytes).RestoreFields(dim2);
            new SnapshotReader().Read(bytes).RestoreSystem(war2);

            bool completed = false;
            war2.OnOccupationComplete = (nodeId, attackerId) =>
            {
                if (nodeId == "mars" && attackerId == "earth_empire") completed = true;
            };

            war2.Tick(2f); // progress += 1.0 → completes
            Assert.IsTrue(completed, "Occupation must complete after resume + tick");
        }

        // ── Faction resume ────────────────────────────────────────────────────

        [Test]
        public void FactionResume_DominanceCorrectAfterRestore()
        {
            var dim = BuildWorld();
            var factions = new FactionSystem(dim, P("fp"), P("fi"), P("fs"));
            factions.AddPresence("earth", "empire_red", 2.0f);
            factions.AddPresence("earth", "rebels",     0.3f);
            factions.AddPresence("mars",  "empire_red", 1.5f);
            factions.AddPresence("belt",  "pirates",    1.0f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = BuildWorld2();
            var factions2 = new FactionSystem(dim2, P("fp"), P("fi"), P("fs"));
            new SnapshotReader().Read(bytes).RestoreFields(dim2);
            factions2.PostLoad();

            Assert.AreEqual("empire_red", factions2.GetDominantFaction("earth"));
            Assert.AreEqual("empire_red", factions2.GetDominantFaction("mars"));
            Assert.AreEqual("pirates",    factions2.GetDominantFaction("belt"));
        }

        [Test]
        public void FactionResume_PostLoad_NoSpuriousCallbacks()
        {
            var dim = BuildWorld();
            var factions = new FactionSystem(dim, P("fp"), P("fi"), P("fs"));
            factions.AddPresence("earth", "empire_red", 2.5f);
            factions.AddPresence("mars",  "empire_red", 1.5f);
            factions.AddPresence("mars",  "rebels",     0.5f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = BuildWorld2();
            var factions2 = new FactionSystem(dim2, P("fp"), P("fi"), P("fs"));
            new SnapshotReader().Read(bytes).RestoreFields(dim2);
            factions2.PostLoad();

            var spurious = new List<string>();
            factions2.OnDominanceChanged = (nodeId, _, _) => spurious.Add(nodeId);
            factions2.Tick(0.00001f); // effectively no propagation

            Assert.AreEqual(0, spurious.Count,
                "PostLoad must suppress false-positive callbacks on first tick after resume");
        }

        // ── Economy resume ────────────────────────────────────────────────────

        [Test]
        public void EconomyResume_FieldsPreservedExactly()
        {
            var dim = BuildWorld();
            var economy = new EconomySystem(dim, P("ep"));
            economy.InjectTrade("earth", "water", 50f);
            economy.InjectTrade("mars",  "ore",   30f);

            float priceEarth = economy.SamplePrice("earth", "water", 5f);
            float priceMars  = economy.SamplePrice("mars",  "ore",   3f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = BuildWorld2();
            var economy2 = new EconomySystem(dim2, P("ep"));
            new SnapshotReader().Read(bytes).RestoreFields(dim2);

            Assert.AreEqual(priceEarth, economy2.SamplePrice("earth", "water", 5f), 1e-4f,
                "Earth water price must match after restore");
            Assert.AreEqual(priceMars,  economy2.SamplePrice("mars",  "ore",   3f), 1e-4f,
                "Mars ore price must match after restore");
        }

        // ── GetOrCreateField resume safety ────────────────────────────────────

        [Test]
        public void GetOrCreateField_SafeOnResume_DoesNotThrow()
        {
            var dim = BuildWorld();
            var war = new WarSystem(dim, P("war"));
            war.Exposure.SetLogAmp("earth", "x", 1.5f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new ISnapshotParticipant[] { war });

            var dim2 = BuildWorld2();
            var snap = new SnapshotReader().Read(bytes);
            snap.RestoreFields(dim2); // Creates "war.exposure" in dim2

            // WarSystem constructor calls GetOrCreateField — must NOT throw even though field exists
            WarSystem war2 = null;
            Assert.DoesNotThrow(() =>
            {
                war2 = new WarSystem(dim2, P("war")); // GetOrCreateField returns existing
                snap.RestoreSystem(war2);
            }, "Constructing system after RestoreFields must not throw");

            Assert.AreEqual(1.5f, war2.GetExposureLogAmp("earth"), 1e-6f,
                "Field data must be intact after safe GetOrCreateField on resume");
        }

        [Test]
        public void GetOrCreateField_EconomyResume_SafeAfterRestoreFields()
        {
            var dim = BuildWorld();
            var economy = new EconomySystem(dim, P("ep"));
            economy.Availability.SetLogAmp("earth", "food", 0.5f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = BuildWorld2();
            new SnapshotReader().Read(bytes).RestoreFields(dim2);

            // Economy constructor after RestoreFields → GetOrCreateField returns existing
            EconomySystem economy2 = null;
            Assert.DoesNotThrow(() => economy2 = new EconomySystem(dim2, P("ep")));
            Assert.AreEqual(0.5f, economy2.Availability.GetLogAmp("earth", "food"), 1e-6f);
        }

        // ── ReconstructDimension ───────────────────────────────────────────────

        [Test]
        public void ReconstructDimension_FullSnapshot_GraphAndFieldsExact()
        {
            var dim = BuildWorld();
            var f = dim.AddField("data", P("data"));
            f.SetLogAmp("earth", "signal", 3.0f);
            f.SetLogAmp("belt",  "noise",  1.5f);

            var restored = new SnapshotReader().Read(
                new SnapshotWriter().WriteFull(dim, 1, 0)).ReconstructDimension();

            // Graph
            Assert.IsNotNull(restored.Graph.Nodes["earth"]);
            Assert.IsNotNull(restored.Graph.Nodes["mars"]);
            Assert.IsNotNull(restored.Graph.Nodes["belt"]);
            Assert.AreEqual(1, restored.Graph.GetOutEdgesSorted("earth").Count);
            Assert.AreEqual(1, restored.Graph.GetOutEdgesSorted("mars").Count);

            // Fields
            var rf = restored.GetField("data");
            Assert.AreEqual(3.0f, rf.GetLogAmp("earth", "signal"), 1e-6f);
            Assert.AreEqual(1.5f, rf.GetLogAmp("belt",  "noise"),  1e-6f);
            Assert.AreEqual(0f,   rf.GetLogAmp("mars",  "signal"), 1e-9f);
        }

        // ── DeltaIndex full run-recording workflow ─────────────────────────────

        [Test]
        public void DeltaIndex_RunRecording_ReadAtTickCorrectAtEveryTick()
        {
            var dim = BuildWorld();
            var f = dim.AddField("sim", P("sim"));
            var writer = new SnapshotWriter();
            var reader = new SnapshotReader();

            var snapshots = new List<byte[]>();
            byte[] prev;

            // Tick 0: Full baseline
            f.SetLogAmp("earth", "pressure", 0.5f);
            prev = writer.WriteFull(dim, 0, 0.0);
            snapshots.Add(prev);

            // Ticks 1–4: Deltas
            for (ulong tick = 1; tick <= 4; tick++)
            {
                f.SetLogAmp("earth", "pressure", 0.5f + tick * 0.1f);
                f.SetLogAmp("mars",  "signal",   tick * 0.2f);
                var delta = writer.WriteDelta(dim, prev, tick, tick * 1.0, tick - 1, (ushort)tick);
                snapshots.Add(delta);
                prev = delta;
            }

            // Verify at each tick
            for (ulong tick = 0; tick <= 4; tick++)
            {
                var at = reader.ReadAtTick(snapshots, tick).ReconstructDimension();
                float expectedPressure = 0.5f + tick * 0.1f;
                float expectedSignal   = tick * 0.2f;
                Assert.AreEqual(expectedPressure, at.GetField("sim").GetLogAmp("earth", "pressure"), 1e-5f,
                    $"earth pressure at tick {tick}");
                if (tick == 0)
                    Assert.AreEqual(0f, at.GetField("sim").GetLogAmp("mars", "signal"), 1e-9f,
                        $"mars signal at tick 0 (never set)");
                else
                    Assert.AreEqual(expectedSignal, at.GetField("sim").GetLogAmp("mars", "signal"), 1e-5f,
                        $"mars signal at tick {tick}");
            }
        }

        [Test]
        public void DeltaIndex_CheckpointInSeries_UsedAsBase()
        {
            var dim = BuildWorld();
            var f = dim.AddField("f", P("f"));
            f.SetLogAmp("earth", "x", 1.0f);
            var writer = new SnapshotWriter();

            // Full at tick 0, Checkpoint at tick 5, Delta at tick 6
            var full = writer.WriteFull(dim, 0, 0.0);
            f.SetLogAmp("earth", "x", 5.0f);
            var checkpoint = writer.WriteCheckpoint(dim, 5, 5.0, new List<ISnapshotParticipant>());
            f.SetLogAmp("earth", "x", 6.0f);
            var delta = writer.WriteDelta(dim, checkpoint, 6, 6.0, 5, 1);

            var series = new List<byte[]> { full, checkpoint, delta };
            var at6 = new SnapshotReader().ReadAtTick(series, 6).ReconstructDimension();

            Assert.AreEqual(6.0f, at6.GetField("f").GetLogAmp("earth", "x"), 1e-5f,
                "ReadAtTick must use Checkpoint as base and apply Delta on top");
        }

        // ── Multi-system checkpoint ────────────────────────────────────────────

        [Test]
        public void MultiSystem_WarPlusFactionPlusEconomy_AllRoundTrip()
        {
            var dim = BuildWorld();

            // War
            var war = new WarSystem(dim, P("war"));
            war.DeclareWar("earth");
            war.DeclareOccupation("mars", "faction_red");
            war.SetNodeStability("belt", 0.8f);
            war.Tick(1f);

            // Economy
            var economy = new EconomySystem(dim, P("ep"));
            economy.InjectTrade("earth", "water", 20f);
            economy.InjectTrade("mars",  "ore",   10f);

            // Faction
            var factions = new FactionSystem(dim, P("fp"), P("fi"), P("fs"));
            factions.AddPresence("earth", "faction_red", 2.0f);
            factions.AddPresence("mars",  "faction_blue", 1.5f);

            // Checkpoint — only WarSystem has a blob; others are field-only
            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 10, 10.0,
                new ISnapshotParticipant[] { war });

            // Restore
            var dim2 = BuildWorld2();
            var snap  = new SnapshotReader().Read(bytes);
            snap.RestoreFields(dim2);

            var war2     = new WarSystem(dim2, P("war"));
            var economy2 = new EconomySystem(dim2, P("ep"));
            var factions2 = new FactionSystem(dim2, P("fp"), P("fi"), P("fs"));
            snap.RestoreSystem(war2);
            factions2.PostLoad();

            // War assertions
            Assert.IsTrue(war2.IsAtWar("earth"));
            Assert.AreEqual("faction_red", war2.GetOccupationAttacker("mars"));
            Assert.AreEqual(0.8f, war2.GetNodeStability("belt"), 1e-6f);
            Assert.Greater(war2.GetExposureLogAmp("earth"), 0f);

            // Economy assertions (via field access)
            Assert.Greater(economy2.PricePressure.GetLogAmp("earth", "water"), 0f);
            Assert.Greater(economy2.Availability.GetLogAmp("mars", "ore"), 0f,
                "Note: availability logAmp is negative (reduced), abs > 0");

            // Faction assertions
            Assert.AreEqual("faction_red",  factions2.GetDominantFaction("earth"));
            Assert.AreEqual("faction_blue", factions2.GetDominantFaction("mars"));
        }

        // ── Profile preservation ───────────────────────────────────────────────

        [Test]
        public void FieldProfile_LogEpsilon_RestoredFromCheckpoint()
        {
            // LogEpsilon is a FieldProfile value — must round-trip through serialization
            var dim = BuildWorld();
            var profile = new FieldProfile("war") { LogEpsilon = 0.0003f };
            var war = new WarSystem(dim, profile);
            war.Exposure.SetLogAmp("earth", "x", 1.0f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new ISnapshotParticipant[] { war });
            var dim2 = BuildWorld2();
            new SnapshotReader().Read(bytes).RestoreFields(dim2);

            Assert.AreEqual(0.0003f, dim2.GetField("war.exposure").Profile.LogEpsilon, 1e-7f,
                "LogEpsilon must be restored from snapshot so resumed simulation uses identical precision");
        }

        // ── Full → Delta → Checkpoint chain ───────────────────────────────────

        [Test]
        public void FullDeltaCheckpoint_TripleChain_CorrectAtEachPoint()
        {
            var dim = BuildWorld();
            var f = dim.AddField("f", P("f"));
            f.SetLogAmp("earth", "x", 1.0f);
            var writer = new SnapshotWriter();
            var series = new List<byte[]>();
            byte[] prev;

            // Tick 0: Full
            prev = writer.WriteFull(dim, 0, 0.0);
            series.Add(prev);

            // Tick 1-3: Deltas
            for (ulong tick = 1; tick <= 3; tick++)
            {
                f.SetLogAmp("earth", "x", tick + 1f);
                var delta = writer.WriteDelta(dim, prev, tick, (double)tick, tick - 1, (ushort)tick);
                series.Add(delta);
                prev = delta;
            }

            // Tick 4: Checkpoint (a Full with blobs)
            f.SetLogAmp("earth", "x", 5.0f);
            var checkpoint = writer.WriteCheckpoint(dim, 4, 4.0, new List<ISnapshotParticipant>());
            series.Add(checkpoint);
            prev = checkpoint;

            // Tick 5-6: More Deltas after checkpoint
            for (ulong tick = 5; tick <= 6; tick++)
            {
                f.SetLogAmp("earth", "x", tick + 1f);
                var delta = writer.WriteDelta(dim, prev, tick, (double)tick, tick - 1, (ushort)(tick - 3));
                series.Add(delta);
                prev = delta;
            }

            var reader = new SnapshotReader();

            // Check tick 2 (before checkpoint)
            Assert.AreEqual(3.0f, reader.ReadAtTick(series, 2).ReconstructDimension()
                .GetField("f").GetLogAmp("earth", "x"), 1e-5f, "Tick 2 value");

            // Check tick 4 (at checkpoint)
            Assert.AreEqual(5.0f, reader.ReadAtTick(series, 4).ReconstructDimension()
                .GetField("f").GetLogAmp("earth", "x"), 1e-5f, "Tick 4 value");

            // Check tick 6 (after checkpoint delta)
            Assert.AreEqual(7.0f, reader.ReadAtTick(series, 6).ReconstructDimension()
                .GetField("f").GetLogAmp("earth", "x"), 1e-5f, "Tick 6 value");
        }
    }
}
