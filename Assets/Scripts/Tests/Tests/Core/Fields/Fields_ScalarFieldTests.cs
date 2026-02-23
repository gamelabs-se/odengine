using System;
using NUnit.Framework;
using Odengine.Fields;

namespace Odengine.Tests.Core.Fields
{
    [TestFixture]
    public class Fields_ScalarFieldTests
    {
        private static FieldProfile DefaultProfile() =>
            new FieldProfile("test.field") { PropagationRate = 1f, EdgeResistanceScale = 1f, DecayRate = 0f };

        // ── Neutral baseline ────────────────────────────────────────────────

        [Test]
        public void NeutralBaseline_UnsetNodeAndChannel_MultiplierIsOne()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.AreEqual(1f, field.GetMultiplier("node", "ch"), 1e-6f);
        }

        [Test]
        public void NeutralBaseline_UnsetNode_LogAmpIsZero()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.AreEqual(0f, field.GetLogAmp("node", "ch"), 1e-9f);
        }

        [Test]
        public void NeutralBaseline_EmptyChannelSet_WhenNothingSet()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void NeutralBaseline_EnumerateAllActiveSorted_EmptyWhenNothingSet()
        {
            var field = new ScalarField("f", DefaultProfile());
            int count = 0;
            foreach (var _ in field.EnumerateAllActiveSorted()) count++;
            Assert.AreEqual(0, count);
        }

        // ── SetLogAmp — storage and retrieval ───────────────────────────────

        [Test]
        public void SetLogAmp_AboveEpsilon_IsStored()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 1f);
            Assert.AreEqual(1f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        [Test]
        public void SetLogAmp_ExactlyZero_IsRemovedFromStorage()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 1f);  // store something
            field.SetLogAmp("n", "ch", 0f);  // then clear
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void SetLogAmp_BelowEpsilon_IsRemovedFromStorage()
        {
            // LogEpsilon = 0.0001f — values below this are treated as neutral
            const float belowEpsilon = 0.00005f;
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 1f);       // establish entry
            field.SetLogAmp("n", "ch", belowEpsilon);  // set below threshold
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count,
                "Entry below LogEpsilon must be pruned from sparse storage");
        }

        [Test]
        public void SetLogAmp_NegativeBelowEpsilon_IsRemovedFromStorage()
        {
            const float belowEpsilon = -0.00005f;
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", -1f);
            field.SetLogAmp("n", "ch", belowEpsilon);
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void SetLogAmp_Twice_Overwrites()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 1f);
            field.SetLogAmp("n", "ch", 3f);
            Assert.AreEqual(3f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        [Test]
        public void SetLogAmp_NullNodeId_NoOp()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.DoesNotThrow(() => field.SetLogAmp(null, "ch", 1f));
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void SetLogAmp_NullChannelId_NoOp()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.DoesNotThrow(() => field.SetLogAmp("n", null, 1f));
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void SetLogAmp_EmptyNodeId_NoOp()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.DoesNotThrow(() => field.SetLogAmp("", "ch", 1f));
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void SetLogAmp_ClampsAtMaxLogAmp()
        {
            var profile = new FieldProfile("f") { MaxLogAmpClamp = 5f, MinLogAmpClamp = -20f };
            var field = new ScalarField("f", profile);
            field.SetLogAmp("n", "ch", 100f);
            Assert.AreEqual(5f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        [Test]
        public void SetLogAmp_ClampsAtMinLogAmp()
        {
            var profile = new FieldProfile("f") { MaxLogAmpClamp = 20f, MinLogAmpClamp = -5f };
            var field = new ScalarField("f", profile);
            field.SetLogAmp("n", "ch", -100f);
            Assert.AreEqual(-5f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        // ── AddLogAmp ───────────────────────────────────────────────────────

        [Test]
        public void AddLogAmp_OnNeutral_CreatesEntry()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.AddLogAmp("n", "ch", 1f);
            Assert.AreEqual(1f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        [Test]
        public void AddLogAmp_Accumulates()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.AddLogAmp("n", "ch", 1f);
            field.AddLogAmp("n", "ch", 2f);
            Assert.AreEqual(3f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        [Test]
        public void AddLogAmp_NegativeOnPositive_Cancels()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.AddLogAmp("n", "ch", 2f);
            field.AddLogAmp("n", "ch", -2f);
            // Result is 0, which is < LogEpsilon → entry removed
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
            Assert.AreEqual(0f, field.GetLogAmp("n", "ch"), 1e-9f);
        }

        [Test]
        public void AddLogAmp_BelowEpsilonDelta_IsIgnored()
        {
            // AddLogAmp early-exits if |delta| < LogEpsilon
            const float tinyDelta = 0.00005f;
            var field = new ScalarField("f", DefaultProfile());
            field.AddLogAmp("n", "ch", tinyDelta);
            // Nothing should be stored
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void AddLogAmp_NullNodeId_NoOp()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.DoesNotThrow(() => field.AddLogAmp(null, "ch", 1f));
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void AddLogAmp_NullChannelId_NoOp()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.DoesNotThrow(() => field.AddLogAmp("n", null, 1f));
            Assert.AreEqual(0, field.GetActiveChannelIdsSorted().Count);
        }

        // ── GetMultiplier algebra ────────────────────────────────────────────

        [Test]
        public void GetMultiplier_EqualsExpOfLogAmp()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 2.5f);
            float expected = MathF.Exp(2.5f);
            Assert.AreEqual(expected, field.GetMultiplier("n", "ch"), 1e-5f);
        }

        [Test]
        public void GetMultiplier_PositiveLogAmp_GreaterThanOne()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 1f);
            Assert.Greater(field.GetMultiplier("n", "ch"), 1f);
        }

        [Test]
        public void GetMultiplier_NegativeLogAmp_LessThanOne()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", -1f);
            Assert.Less(field.GetMultiplier("n", "ch"), 1f);
            Assert.Greater(field.GetMultiplier("n", "ch"), 0f);
        }

        [Test]
        public void GetMultiplier_IsAlwaysPositive()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", -20f); // near min clamp
            Assert.Greater(field.GetMultiplier("n", "ch"), 0f);
        }

        [Test]
        public void AddLogAmpAlgebra_AddTwoLogAmps_EqualsMultiplyMultipliers()
        {
            // logAmp(a + b) == log(exp(a) * exp(b))
            var field = new ScalarField("f", DefaultProfile());
            field.AddLogAmp("n", "ch", 1f);
            field.AddLogAmp("n", "ch", 0.5f);
            float expected = MathF.Exp(1f) * MathF.Exp(0.5f);
            Assert.AreEqual(expected, field.GetMultiplier("n", "ch"), 1e-5f);
        }

        // ── Channel and node isolation ───────────────────────────────────────

        [Test]
        public void IndependentChannels_DontInterfere()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch-a", 2f);
            field.SetLogAmp("n", "ch-b", -1f);
            Assert.AreEqual(2f, field.GetLogAmp("n", "ch-a"), 1e-6f);
            Assert.AreEqual(-1f, field.GetLogAmp("n", "ch-b"), 1e-6f);
        }

        [Test]
        public void IndependentNodes_DontInterfere()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n1", "ch", 3f);
            field.SetLogAmp("n2", "ch", -0.5f);
            Assert.AreEqual(3f, field.GetLogAmp("n1", "ch"), 1e-6f);
            Assert.AreEqual(-0.5f, field.GetLogAmp("n2", "ch"), 1e-6f);
        }

        [Test]
        public void ManyChannelsSameNode_AllIndependent()
        {
            var field = new ScalarField("f", DefaultProfile());
            for (int i = 0; i < 50; i++)
                field.SetLogAmp("n", $"ch{i:000}", i * 0.1f);

            for (int i = 0; i < 50; i++)
                Assert.AreEqual(i * 0.1f, field.GetLogAmp("n", $"ch{i:000}"), 1e-5f,
                    $"Channel ch{i:000} was corrupted");
        }

        [Test]
        public void ManyNodesSameChannel_AllIndependent()
        {
            var field = new ScalarField("f", DefaultProfile());
            for (int i = 0; i < 50; i++)
                field.SetLogAmp($"node{i:000}", "ch", i * 0.1f);

            for (int i = 0; i < 50; i++)
                Assert.AreEqual(i * 0.1f, field.GetLogAmp($"node{i:000}", "ch"), 1e-5f);
        }

        // ── GetActiveChannelIdsSorted ────────────────────────────────────────

        [Test]
        public void GetActiveChannelIdsSorted_AfterSet_ContainsChannel()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "water", 1f);
            CollectionAssert.Contains(field.GetActiveChannelIdsSorted(), "water");
        }

        [Test]
        public void GetActiveChannelIdsSorted_AfterClearToNeutral_ChannelGone()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "water", 1f);
            field.SetLogAmp("n", "water", 0f);
            CollectionAssert.DoesNotContain(field.GetActiveChannelIdsSorted(), "water");
        }

        [Test]
        public void GetActiveChannelIdsSorted_IsOrdinalSorted()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "zzz", 1f);
            field.SetLogAmp("n", "aaa", 1f);
            field.SetLogAmp("n", "mmm", 1f);

            var ids = field.GetActiveChannelIdsSorted();
            Assert.AreEqual("aaa", ids[0]);
            Assert.AreEqual("mmm", ids[1]);
            Assert.AreEqual("zzz", ids[2]);
        }

        [Test]
        public void GetActiveChannelIdsSorted_NoDuplicates()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n1", "ch", 1f);
            field.SetLogAmp("n2", "ch", 2f);

            // same channel on two nodes — should appear once
            Assert.AreEqual(1, field.GetActiveChannelIdsSorted().Count);
        }

        [Test]
        public void GetActiveChannelIdsSorted_MultipleChannels_AllPresent()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "a", 1f);
            field.SetLogAmp("n", "b", 1f);
            field.SetLogAmp("n", "c", 1f);
            Assert.AreEqual(3, field.GetActiveChannelIdsSorted().Count);
        }

        // ── GetActiveNodeIdsSortedForChannel ────────────────────────────────

        [Test]
        public void GetActiveNodeIdsSortedForChannel_ReturnsCorrectNodes()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("earth", "water", 1f);
            field.SetLogAmp("mars", "water", 2f);

            var nodes = field.GetActiveNodeIdsSortedForChannel("water");
            CollectionAssert.Contains(nodes, "earth");
            CollectionAssert.Contains(nodes, "mars");
        }

        [Test]
        public void GetActiveNodeIdsSortedForChannel_IsOrdinalSorted()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("z-node", "ch", 1f);
            field.SetLogAmp("a-node", "ch", 1f);
            field.SetLogAmp("m-node", "ch", 1f);

            var nodes = field.GetActiveNodeIdsSortedForChannel("ch");
            Assert.AreEqual("a-node", nodes[0]);
            Assert.AreEqual("m-node", nodes[1]);
            Assert.AreEqual("z-node", nodes[2]);
        }

        [Test]
        public void GetActiveNodeIdsSortedForChannel_OtherChannelNotIncluded()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch-a", 1f);
            field.SetLogAmp("n", "ch-b", 1f);

            var forA = field.GetActiveNodeIdsSortedForChannel("ch-a");
            Assert.AreEqual(1, forA.Count);
            Assert.AreEqual("n", forA[0]);
        }

        [Test]
        public void GetActiveNodeIdsSortedForChannel_EmptyWhenNeutral()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.AreEqual(0, field.GetActiveNodeIdsSortedForChannel("ch").Count);
        }

        // ── EnumerateAllActiveSorted ─────────────────────────────────────────

        [Test]
        public void EnumerateAllActiveSorted_ContainsAllSetEntries()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n1", "ch-a", 1f);
            field.SetLogAmp("n2", "ch-b", 2f);

            int count = 0;
            foreach (var _ in field.EnumerateAllActiveSorted()) count++;
            Assert.AreEqual(2, count);
        }

        [Test]
        public void EnumerateAllActiveSorted_OrderedByChannelThenNode()
        {
            var field = new ScalarField("f", DefaultProfile());
            // Insert in reverse order to verify sort
            field.SetLogAmp("z-node", "b-chan", 1f);
            field.SetLogAmp("a-node", "b-chan", 2f);
            field.SetLogAmp("m-node", "a-chan", 3f);

            var entries = new System.Collections.Generic.List<(string nodeId, string channelId, float logAmp)>();
            foreach (var e in field.EnumerateAllActiveSorted()) entries.Add(e);

            // a-chan comes before b-chan; within b-chan, a-node comes before z-node
            Assert.AreEqual("a-chan", entries[0].channelId);
            Assert.AreEqual("m-node", entries[0].nodeId);
            Assert.AreEqual("b-chan", entries[1].channelId);
            Assert.AreEqual("a-node", entries[1].nodeId);
            Assert.AreEqual("b-chan", entries[2].channelId);
            Assert.AreEqual("z-node", entries[2].nodeId);
        }

        [Test]
        public void EnumerateAllActiveSorted_IsStableAcrossCallsWithSameState()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n1", "ch1", 1f);
            field.SetLogAmp("n2", "ch2", 2f);

            var a = new System.Collections.Generic.List<string>();
            var b = new System.Collections.Generic.List<string>();
            foreach (var (nodeId, channelId, _) in field.EnumerateAllActiveSorted())
                a.Add($"{channelId}:{nodeId}");
            foreach (var (nodeId, channelId, _) in field.EnumerateAllActiveSorted())
                b.Add($"{channelId}:{nodeId}");
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void EnumerateAllActiveSorted_LogAmpMatchesGetLogAmp()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 2.7f);
            foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
            {
                Assert.AreEqual(field.GetLogAmp(nodeId, channelId), logAmp, 1e-9f);
            }
        }

        // ── ForChannel / ChannelView facade ──────────────────────────────────

        [Test]
        public void ForChannel_ReturnsViewForChannel()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "water", 1.5f);
            var view = field.ForChannel("water");
            Assert.AreEqual(1.5f, view.GetLogAmp("n"), 1e-6f);
        }

        [Test]
        public void ForChannel_WriteThroughView_ReflectedInField()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ch");
            view.AddLogAmp("n", 2f);
            Assert.AreEqual(2f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        // ── Constructor validation ───────────────────────────────────────────

        [Test]
        public void Constructor_NullFieldId_Throws()
        {
            // Both null and empty throw ArgumentException ("cannot be null or empty")
            Assert.Throws<ArgumentException>(() =>
                new ScalarField(null, DefaultProfile()));
        }

        [Test]
        public void Constructor_EmptyFieldId_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ScalarField("", DefaultProfile()));
        }

        [Test]
        public void Constructor_NullProfile_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ScalarField("f", null));
        }

        [Test]
        public void FieldId_StoredCorrectly()
        {
            var field = new ScalarField("economy.availability", DefaultProfile());
            Assert.AreEqual("economy.availability", field.FieldId);
        }

        [Test]
        public void Profile_StoredCorrectly()
        {
            var profile = DefaultProfile();
            var field = new ScalarField("f", profile);
            Assert.AreSame(profile, field.Profile);
        }

        // ── Extreme / edge cases ─────────────────────────────────────────────

        [Test]
        public void ExtremelyLargePositiveValue_ClampsToMax_NoNaN()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", float.MaxValue);
            float val = field.GetLogAmp("n", "ch");
            Assert.IsFalse(float.IsNaN(val), "logAmp must not be NaN");
            Assert.IsFalse(float.IsInfinity(val), "logAmp must not be Infinity");
        }

        [Test]
        public void ExtremelyLargeNegativeValue_ClampsToMin_NoNaN()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", float.MinValue);
            float val = field.GetLogAmp("n", "ch");
            Assert.IsFalse(float.IsNaN(val));
            Assert.IsFalse(float.IsInfinity(val));
        }

        [Test]
        public void GetMultiplier_AfterClampedValue_IsPositive()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", -1000f); // well below MinLogAmpClamp
            Assert.Greater(field.GetMultiplier("n", "ch"), 0f);
        }

        // ── GetDominantChannel ──────────────────────────────────────────────

        [Test]
        public void GetDominantChannel_EmptyNode_ReturnsNull()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.That(field.GetDominantChannel("node"), Is.Null);
        }

        [Test]
        public void GetDominantChannel_NullNodeId_ReturnsNull()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.That(field.GetDominantChannel(null), Is.Null);
        }

        [Test]
        public void GetDominantChannel_EmptyNodeId_ReturnsNull()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.That(field.GetDominantChannel(""), Is.Null);
        }

        [Test]
        public void GetDominantChannel_SinglePositiveChannel_ReturnsIt()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "faction_a", 1.5f);
            Assert.That(field.GetDominantChannel("n"), Is.EqualTo("faction_a"));
        }

        [Test]
        public void GetDominantChannel_HighestPositiveChannel_Wins()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "faction_a", 1.0f);
            field.SetLogAmp("n", "faction_b", 2.0f);
            field.SetLogAmp("n", "faction_c", 0.5f);
            Assert.That(field.GetDominantChannel("n"), Is.EqualTo("faction_b"));
        }

        [Test]
        public void GetDominantChannel_NegativeChannelOnly_ReturnsNull()
        {
            // Negative logAmp is below neutral baseline — never dominant
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "faction_a", -1.0f);
            Assert.That(field.GetDominantChannel("n"), Is.Null,
                "Negative logAmp is below neutral (0) — not dominant");
        }

        [Test]
        public void GetDominantChannel_MixedPositiveAndNegative_IgnoresNegative()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "faction_a",  1.0f);
            field.SetLogAmp("n", "faction_b", -2.0f); // very negative — should not win
            Assert.That(field.GetDominantChannel("n"), Is.EqualTo("faction_a"));
        }

        [Test]
        public void GetDominantChannel_TieBreaking_OrdinalFirstWins()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "faction_zzz", 1.0f);
            field.SetLogAmp("n", "faction_aaa", 1.0f);
            // Both equal logAmp — "faction_aaa" < "faction_zzz" in Ordinal order
            Assert.That(field.GetDominantChannel("n"), Is.EqualTo("faction_aaa"),
                "Tie-breaking must be deterministic: Ordinal-first channelId wins");
        }

        [Test]
        public void GetDominantChannel_TieBreaking_IsDeterministicRegardlessOfInsertOrder()
        {
            // Insert in reverse order — result must be identical
            var field1 = new ScalarField("f", DefaultProfile());
            field1.SetLogAmp("n", "faction_aaa", 1.0f);
            field1.SetLogAmp("n", "faction_zzz", 1.0f);

            var field2 = new ScalarField("f", DefaultProfile());
            field2.SetLogAmp("n", "faction_zzz", 1.0f);
            field2.SetLogAmp("n", "faction_aaa", 1.0f);

            Assert.That(field1.GetDominantChannel("n"), Is.EqualTo(field2.GetDominantChannel("n")),
                "Tie-breaking result must not depend on insertion order");
        }

        [Test]
        public void GetDominantChannel_OtherNodeChannels_NotConsidered()
        {
            // High logAmp on a different node must not affect the queried node
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("node_a", "faction_a", 0.5f);
            field.SetLogAmp("node_b", "faction_b", 9.9f); // very high — but wrong node
            Assert.That(field.GetDominantChannel("node_a"), Is.EqualTo("faction_a"),
                "Channels on other nodes must not influence the queried node's dominant");
        }

        [Test]
        public void GetDominantChannel_AfterErosionToNeutral_ReturnsNull()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "faction_a", 1.0f);
            field.SetLogAmp("n", "faction_a", 0f); // erase back to neutral
            Assert.That(field.GetDominantChannel("n"), Is.Null,
                "After logAmp returns to neutral (0), node should have no dominant");
        }

        [Test]
        public void GetDominantChannel_ThreeFactionsOneNegativeOneTie_IgnoresNegative()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "empire_red",  1.0f);
            field.SetLogAmp("n", "empire_blue", 1.0f);  // tie with red
            field.SetLogAmp("n", "pirates",    -0.5f);  // below baseline
            // Tie between red and blue — ordinal order: "empire_blue" < "empire_red"
            Assert.That(field.GetDominantChannel("n"), Is.EqualTo("empire_blue"));
        }

        // ── GetActiveChannelIdsSortedForNode ────────────────────────────────

        [Test]
        public void GetActiveChannelIdsSortedForNode_EmptyNode_ReturnsEmpty()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.That(field.GetActiveChannelIdsSortedForNode("n").Count, Is.EqualTo(0));
        }

        [Test]
        public void GetActiveChannelIdsSortedForNode_NullNode_ReturnsEmpty()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.That(field.GetActiveChannelIdsSortedForNode(null).Count, Is.EqualTo(0));
        }

        [Test]
        public void GetActiveChannelIdsSortedForNode_ReturnsOnlyThatNodesChannels()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("node_a", "ch1", 1f);
            field.SetLogAmp("node_a", "ch2", 1f);
            field.SetLogAmp("node_b", "ch3", 1f); // different node

            var channels = field.GetActiveChannelIdsSortedForNode("node_a");
            Assert.That(channels.Count, Is.EqualTo(2));
            Assert.That(channels, Does.Contain("ch1"));
            Assert.That(channels, Does.Contain("ch2"));
            Assert.That(channels, Does.Not.Contain("ch3"));
        }

        [Test]
        public void GetActiveChannelIdsSortedForNode_ResultIsOrdinalSorted()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "zzz", 1f);
            field.SetLogAmp("n", "aaa", 1f);
            field.SetLogAmp("n", "mmm", 1f);

            var channels = field.GetActiveChannelIdsSortedForNode("n");
            Assert.That(channels[0], Is.EqualTo("aaa"));
            Assert.That(channels[1], Is.EqualTo("mmm"));
            Assert.That(channels[2], Is.EqualTo("zzz"));
        }

        // ── GetActiveNodeIdsSorted ──────────────────────────────────────────

        [Test]
        public void GetActiveNodeIdsSorted_EmptyField_ReturnsEmpty()
        {
            var field = new ScalarField("f", DefaultProfile());
            Assert.That(field.GetActiveNodeIdsSorted().Count, Is.EqualTo(0));
        }

        [Test]
        public void GetActiveNodeIdsSorted_MultipleNodes_ReturnsSortedUnique()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("zzz", "ch", 1f);
            field.SetLogAmp("aaa", "ch", 1f);
            field.SetLogAmp("mmm", "ch", 1f);
            field.SetLogAmp("aaa", "ch2", 1f); // same node, second channel

            var nodes = field.GetActiveNodeIdsSorted();
            Assert.That(nodes.Count, Is.EqualTo(3));
            Assert.That(nodes[0], Is.EqualTo("aaa"));
            Assert.That(nodes[1], Is.EqualTo("mmm"));
            Assert.That(nodes[2], Is.EqualTo("zzz"));
        }
    }
}
