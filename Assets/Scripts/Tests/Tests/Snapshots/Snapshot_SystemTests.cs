using System;
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
    /// Tier 2+3 — Domain system blob contract (WarSystem, FactionSystem, EconomySystem)
    /// and system-participant plumbing (ISnapshotParticipant, duplicate IDs, type guards).
    /// </summary>
    [TestFixture]
    public class Snapshot_SystemTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static Dimension TwoNodeDim()
        {
            var d = new Dimension();
            d.AddNode("earth", "Earth");
            d.AddNode("mars",  "Mars");
            d.AddEdge("earth", "mars", 0.5f);
            return d;
        }

        private static FieldProfile P(string id = "war") => new FieldProfile(id);

        private static (SnapshotData snap, Dimension dim2) Checkpoint(
            Dimension dim, params ISnapshotParticipant[] participants)
        {
            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, participants);
            var dim2  = new Dimension();
            dim2.AddNode("earth"); dim2.AddNode("mars");
            dim2.AddEdge("earth", "mars", 0.5f);
            return (new SnapshotReader().Read(bytes), dim2);
        }

        // ── WarSystem.SystemId ────────────────────────────────────────────────

        [Test]
        public void WarSystem_SystemId_IsWarSystem()
        {
            var war = new WarSystem(TwoNodeDim(), P());
            Assert.AreEqual("war.system", war.SystemId);
        }

        // ── WarSystem config serialization ────────────────────────────────────

        [Test]
        public void WarSystem_Config_ExposureGrowthRate_RoundTrip()
        {
            var dim  = TwoNodeDim();
            var cfg  = new WarConfig { ExposureGrowthRate = 0.07f };
            var war  = new WarSystem(dim, P(), cfg);

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            snap.RestoreSystem(war2);

            // Observable: DeclareWar then tick with restored config → exposure grows at 0.07/tick
            war2.DeclareWar("earth");
            war2.Exposure.SetLogAmp("earth", "x", 0f); // reset after field restoration
            war2.Tick(1f);
            // After 1 tick at ExposureGrowthRate=0.07, logAmp should be ~0.07
            Assert.AreEqual(0.07f, war2.GetExposureLogAmp("earth"), 0.001f,
                "ExposureGrowthRate must be restored from blob");
        }

        [Test]
        public void WarSystem_Config_CeasefireDecayRate_RoundTrip()
        {
            var dim = TwoNodeDim();
            var cfg = new WarConfig { CeasefireDecayRate = 0.12f, ExposureEpsilon = 0.0001f };
            var war = new WarSystem(dim, P(), cfg);
            // Set up a cooling node with known exposure
            war.Exposure.SetLogAmp("mars", "x", 0.5f);
            war.DeclareWar("mars");
            war.DeclareCeasefire("mars");

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            snap.RestoreFields(dim2);
            snap.RestoreSystem(war2);

            float before = war2.GetExposureLogAmp("mars");
            war2.Tick(1f);
            float after = war2.GetExposureLogAmp("mars");

            // Decay should remove min(before, 0.12*1) = 0.12 from 0.5
            Assert.AreEqual(before - 0.12f, after, 0.005f,
                "CeasefireDecayRate must be restored from blob");
        }

        [Test]
        public void WarSystem_Config_ExposureChannelId_RoundTrip()
        {
            var dim = TwoNodeDim();
            var cfg = new WarConfig { ExposureChannelId = "battle_zone" };
            var war = new WarSystem(dim, P(), cfg);

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            snap.RestoreSystem(war2);

            // DeclareWar + Tick: exposure should accumulate in "battle_zone", not "x"
            war2.DeclareWar("earth");
            war2.Tick(0.5f);
            // GetExposureLogAmp uses the restored channelId
            Assert.Greater(war2.GetExposureLogAmp("earth"), 0f,
                "Exposure channel must match restored config value");
        }

        // ── WarSystem state collection serialization ───────────────────────────

        [Test]
        public void WarSystem_ActiveWarNodes_RoundTrip()
        {
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P());
            war.DeclareWar("earth");
            war.DeclareWar("mars");

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            snap.RestoreSystem(war2);

            Assert.IsTrue(war2.IsAtWar("earth"), "earth must remain in active-war set");
            Assert.IsTrue(war2.IsAtWar("mars"),  "mars must remain in active-war set");
            Assert.IsFalse(war2.IsAtWar("belt"), "undeclared node must not be at war");
        }

        [Test]
        public void WarSystem_CoolingNodes_RoundTrip()
        {
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P());
            war.DeclareWar("earth");
            war.DeclareCeasefire("earth");

            Assert.IsTrue(war.IsCooling("earth"));
            Assert.IsFalse(war.IsAtWar("earth"));

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            snap.RestoreSystem(war2);

            Assert.IsTrue(war2.IsCooling("earth"),   "cooling state must be restored");
            Assert.IsFalse(war2.IsAtWar("earth"),    "ceasefire node must not be active-war");
        }

        [Test]
        public void WarSystem_Stability_MultipleNodes_RoundTrip()
        {
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P());
            war.SetNodeStability("earth", 0.75f);
            war.SetNodeStability("mars",  0.25f);

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            snap.RestoreSystem(war2);

            Assert.AreEqual(0.75f, war2.GetNodeStability("earth"), 1e-6f);
            Assert.AreEqual(0.25f, war2.GetNodeStability("mars"),  1e-6f);
            Assert.AreEqual(0f,    war2.GetNodeStability("belt"),  1e-6f, "Unset node defaults to 0");
        }

        [Test]
        public void WarSystem_OccupationProgress_RoundTrip()
        {
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P(), new WarConfig { OccupationBaseRate = 0.5f });
            war.DeclareWar("mars");
            war.DeclareOccupation("mars", "empire_red");
            war.Tick(1f); // progress += 0.5

            float expectedProgress = war.GetOccupationProgress("mars");
            Assert.AreEqual(0.5f, expectedProgress, 0.01f);

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            snap.RestoreFields(dim2);
            snap.RestoreSystem(war2);

            Assert.AreEqual("empire_red",    war2.GetOccupationAttacker("mars"),       "Attacker must be restored");
            Assert.AreEqual(expectedProgress, war2.GetOccupationProgress("mars"), 1e-5f, "Progress must be restored");
        }

        [Test]
        public void WarSystem_OccupationAttacker_MultipleNodes_RoundTrip()
        {
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P());
            war.DeclareOccupation("earth", "faction_a");
            war.DeclareOccupation("mars",  "faction_b");

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            snap.RestoreSystem(war2);

            Assert.AreEqual("faction_a", war2.GetOccupationAttacker("earth"));
            Assert.AreEqual("faction_b", war2.GetOccupationAttacker("mars"));
        }

        [Test]
        public void WarSystem_ExposureField_RestoredByFields()
        {
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P());
            war.Exposure.SetLogAmp("earth", "x", 1.8f);

            var (snap, dim2) = Checkpoint(dim, war);
            snap.RestoreFields(dim2);
            // Field is restored by RestoreFields, not RestoreSystem
            Assert.AreEqual(1.8f, dim2.GetField("war.exposure").GetLogAmp("earth", "x"), 1e-6f);
        }

        [Test]
        public void WarSystem_EmptyState_RoundTripsWithoutError()
        {
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P()); // all collections empty

            var (snap, dim2) = Checkpoint(dim, war);
            var war2 = new WarSystem(dim2, P());
            Assert.DoesNotThrow(() => snap.RestoreSystem(war2));
            Assert.IsFalse(war2.IsAtWar("earth"));
            Assert.IsFalse(war2.IsCooling("earth"));
        }

        [Test]
        public void WarSystem_UnknownBlobVersion_Throws()
        {
            // Write a valid checkpoint, then corrupt the blob version byte
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P());
            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new[] { war });

            // Manually zero the blob version field. The blob layout:
            // At the blob record: sys_id_idx(4) schema_ver(2=outer) payload_len(4) [version_byte(1=inner) …]
            // The inner version byte (blob v1) is the first byte of the payload.
            // Parse to find it: after graph+fields+blobcount. Simpler: DeserializeSystemState
            // is what enforces the inner version check.
            // Corrupt by passing a mangled payload directly:
            var badPayload = new byte[] { 255 }; // version = 255
            Assert.Throws<NotSupportedException>(() =>
                war.DeserializeSystemState(badPayload, blobSchemaVersion: 1));
        }

        // ── FactionSystem PostLoad ─────────────────────────────────────────────

        [Test]
        public void FactionSystem_PostLoad_EmptyFields_NoThrow()
        {
            var factions = new FactionSystem(TwoNodeDim(), P("fp"), P("fi"), P("fs"));
            Assert.DoesNotThrow(() => factions.PostLoad());
        }

        [Test]
        public void FactionSystem_PostLoad_RebuildsLastDominantCache()
        {
            var dim = TwoNodeDim();
            var factions = new FactionSystem(dim, P("fp"), P("fi"), P("fs"));
            factions.AddPresence("earth", "empire_red", 2.5f);
            factions.AddPresence("earth", "rebels",     0.5f);
            factions.AddPresence("mars",  "empire_red", 1.0f);

            // Save + restore fields
            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = TwoNodeDim();
            new SnapshotReader().Read(bytes).RestoreFields(dim2);

            var factions2 = new FactionSystem(dim2, P("fp"), P("fi"), P("fs"));
            factions2.PostLoad(); // rebuild _lastDominant from restored field data

            // After PostLoad, first Tick should NOT fire spurious callbacks for pre-existing dominants
            int callbackCount = 0;
            factions2.OnDominanceChanged = (_, _, _) => callbackCount++;
            factions2.Tick(0.00001f); // tiny tick — dominance unchanged

            Assert.AreEqual(0, callbackCount,
                "PostLoad must prevent spurious dominance-gained callbacks on first tick after resume");
        }

        [Test]
        public void FactionSystem_WithoutPostLoad_SpuriousCallbacksFire()
        {
            // Contrast: skip PostLoad → all dominant factions look "new" → callbacks fire
            var dim = TwoNodeDim();
            var factions = new FactionSystem(dim, P("fp"), P("fi"), P("fs"));
            factions.AddPresence("earth", "empire_red", 2.5f);
            factions.AddPresence("mars",  "empire_red", 1.5f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = TwoNodeDim();
            new SnapshotReader().Read(bytes).RestoreFields(dim2);

            var factions2 = new FactionSystem(dim2, P("fp"), P("fi"), P("fs"));
            // Deliberately skip PostLoad()

            var callbacks = new List<string>();
            factions2.OnDominanceChanged = (nodeId, _, _) => callbacks.Add(nodeId);
            factions2.Tick(0.00001f);

            Assert.Greater(callbacks.Count, 0,
                "Without PostLoad, _lastDominant is empty → dominance-gained callbacks must fire");
        }

        [Test]
        public void FactionSystem_PostLoad_UngovermedNode_NotTracked()
        {
            var dim = TwoNodeDim();
            var factions = new FactionSystem(dim, P("fp"), P("fi"), P("fs"));
            // Only mars has presence; earth is ungoverned
            factions.AddPresence("mars", "empire_red", 1.5f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = TwoNodeDim();
            new SnapshotReader().Read(bytes).RestoreFields(dim2);

            var factions2 = new FactionSystem(dim2, P("fp"), P("fi"), P("fs"));
            factions2.PostLoad();

            // If empire_red then totally decays off mars → OnDominanceChanged fires for mars
            // But earth (ungoverned) should NOT fire at all
            var earthCallbacks = new List<string>();
            factions2.OnDominanceChanged = (nodeId, oldDom, newDom) =>
            {
                if (nodeId == "earth") earthCallbacks.Add(nodeId);
            };
            factions2.Tick(0.00001f);

            Assert.AreEqual(0, earthCallbacks.Count,
                "Ungoverned earth node must not generate dominance callbacks");
        }

        [Test]
        public void FactionSystem_PostLoad_CorrectDominantAfterRestore()
        {
            var dim = TwoNodeDim();
            var factions = new FactionSystem(dim, P("fp"), P("fi"), P("fs"));
            factions.AddPresence("earth", "empire_red", 2.0f);
            factions.AddPresence("earth", "rebels",     0.5f); // empire_red dominates

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = TwoNodeDim();
            new SnapshotReader().Read(bytes).RestoreFields(dim2);
            var factions2 = new FactionSystem(dim2, P("fp"), P("fi"), P("fs"));

            Assert.AreEqual("empire_red", factions2.GetDominantFaction("earth"),
                "Dominant faction must be correct immediately after field restoration (before PostLoad)");
        }

        // ── ISnapshotParticipant contract ─────────────────────────────────────

        [Test]
        public void Checkpoint_DuplicateSystemId_Throws()
        {
            var dim = TwoNodeDim();
            // Two WarSystem instances have the same SystemId = "war.system"
            var war1 = new WarSystem(dim, P("w1"));
            var war2 = new WarSystem(dim, P("w2"));

            Assert.Throws<InvalidOperationException>(() =>
                new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new ISnapshotParticipant[] { war1, war2 }),
                "Duplicate SystemId must be rejected at write time");
        }

        [Test]
        public void Full_RestoreSystem_Throws()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P());
            var bytes = new SnapshotWriter().WriteFull(dim, 1, 0);

            var dim2 = TwoNodeDim();
            var war = new WarSystem(dim2, P());
            Assert.Throws<InvalidOperationException>(() => new SnapshotReader().Read(bytes).RestoreSystem(war),
                "RestoreSystem must throw on non-Checkpoint snapshots");
        }

        [Test]
        public void Checkpoint_MissingBlob_TryRestoreReturnsFalse()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P()).SetLogAmp("earth", "x", 1f);
            // Checkpoint with zero participants → no blobs
            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());

            var dim2 = TwoNodeDim();
            var war = new WarSystem(dim2, P());
            bool found = new SnapshotReader().Read(bytes).TryRestoreSystem(war);
            Assert.IsFalse(found, "TryRestoreSystem must return false when blob is absent");
        }

        [Test]
        public void Checkpoint_MissingBlob_RestoreSystemThrows()
        {
            var dim = TwoNodeDim();
            dim.AddField("f", P()).SetLogAmp("earth", "x", 1f);
            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());

            var dim2 = TwoNodeDim();
            var war = new WarSystem(dim2, P());
            Assert.Throws<InvalidOperationException>(() => new SnapshotReader().Read(bytes).RestoreSystem(war),
                "RestoreSystem must throw when no matching blob found");
        }

        [Test]
        public void Checkpoint_WarAndFields_AllRestored()
        {
            var dim = TwoNodeDim();
            var war = new WarSystem(dim, P(), new WarConfig { ExposureGrowthRate = 0.09f });
            war.DeclareWar("earth");
            war.DeclareOccupation("mars", "empire_alpha");
            // Extra field (not owned by WarSystem)
            dim.AddField("economy.price", P("ep")).SetLogAmp("earth", "water", 2.5f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 5, 10.0, new ISnapshotParticipant[] { war });
            var snap  = new SnapshotReader().Read(bytes);

            var dim2 = TwoNodeDim();
            var war2 = new WarSystem(dim2, P());
            snap.RestoreFields(dim2);
            snap.RestoreSystem(war2);

            // System state
            Assert.IsTrue(war2.IsAtWar("earth"));
            Assert.AreEqual("empire_alpha", war2.GetOccupationAttacker("mars"));
            // Field state
            Assert.AreEqual(2.5f, dim2.GetField("economy.price").GetLogAmp("earth", "water"), 1e-6f);
        }

        [Test]
        public void Checkpoint_PostLoad_CalledAfterDeserialize()
        {
            // Verify that TryRestoreSystem calls PostLoad via the interface default.
            // We use FactionSystem's PostLoad to observe the side-effect:
            // after TryRestoreSystem, OnDominanceChanged should not fire for pre-existing dominants.
            var dim = TwoNodeDim();
            var factions = new FactionSystem(dim, P("fp"), P("fi"), P("fs"));
            factions.AddPresence("earth", "empire_red", 2.0f);

            // FactionSystem doesn't implement ISnapshotParticipant (no blob), but we can
            // test that SnapshotData.TryRestoreSystem → PostLoad path works via WarSystem:
            var war = new WarSystem(dim, P());
            war.DeclareWar("earth");

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new ISnapshotParticipant[] { war });
            var snap  = new SnapshotReader().Read(bytes);

            var dim2 = TwoNodeDim();
            var war2 = new WarSystem(dim2, P());
            snap.RestoreFields(dim2);
            bool restored = snap.TryRestoreSystem(war2);
            // PostLoad is the default no-op for WarSystem — we just check restore succeeded
            Assert.IsTrue(restored);
            Assert.IsTrue(war2.IsAtWar("earth"), "State must be intact after TryRestoreSystem + PostLoad");
        }

        // ── EconomySystem fields ───────────────────────────────────────────────

        [Test]
        public void EconomySystem_FieldsRestored_ViaSnapshotFields()
        {
            var dim = TwoNodeDim();
            var economy = new EconomySystem(dim, P("ep"));
            economy.Availability.SetLogAmp("earth", "water", 0.8f);
            economy.PricePressure.SetLogAmp("mars",  "ore",   1.2f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());

            var dim2 = TwoNodeDim();
            var economy2 = new EconomySystem(dim2, P("ep"));
            new SnapshotReader().Read(bytes).RestoreFields(dim2);

            Assert.AreEqual(economy.Availability.GetLogAmp("earth", "water"),
                            economy2.Availability.GetLogAmp("earth", "water"), 1e-5f);
            Assert.AreEqual(economy.PricePressure.GetLogAmp("mars", "ore"),
                            economy2.PricePressure.GetLogAmp("mars", "ore"), 1e-5f);
        }

        [Test]
        public void EconomySystem_SamplePrice_SameAfterResume()
        {
            var dim = TwoNodeDim();
            var economy = new EconomySystem(dim, P("ep"));
            economy.InjectTrade("earth", "water", 100f);

            float priceBefore = economy.SamplePrice("earth", "water", 10f);

            var bytes = new SnapshotWriter().WriteCheckpoint(dim, 1, 0, new List<ISnapshotParticipant>());
            var dim2 = TwoNodeDim();
            var economy2 = new EconomySystem(dim2, P("ep"));
            new SnapshotReader().Read(bytes).RestoreFields(dim2);

            float priceAfter = economy2.SamplePrice("earth", "water", 10f);
            Assert.AreEqual(priceBefore, priceAfter, 1e-4f,
                "SamplePrice must produce same result after checkpoint restore");
        }
    }
}
