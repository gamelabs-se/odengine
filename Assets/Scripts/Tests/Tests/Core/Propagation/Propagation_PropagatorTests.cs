using System;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Tests.Core.Propagation
{
    /// <summary>
    /// Tests for <see cref="Propagator.Step"/>.
    ///
    /// Transmission formula:
    ///   delta = sourceLogAmp × exp(-Resistance × EdgeResistanceScale) × PropagationRate × dt
    ///
    /// Double-buffer: all deltas accumulate first, then are applied together —
    /// so a chain A→B→C with one Step cannot let C receive from A in that same tick.
    /// </summary>
    [TestFixture]
    public class Propagation_PropagatorTests
    {
        // ─── Helpers ────────────────────────────────────────────────────────

        private static Dimension MakeLinearChain(
            string[] ids, float resistance = 0f, float dt = 1f,
            float propagationRate = 1f, float edgeResistanceScale = 1f, float decayRate = 0f,
            params string[] edgeTags)
        {
            var dim = new Dimension();
            foreach (var id in ids) dim.AddNode(id);
            for (int i = 0; i < ids.Length - 1; i++)
                dim.AddEdge(ids[i], ids[i + 1], resistance, edgeTags);
            return dim;
        }

        private static ScalarField MakeField(Dimension dim, float propagationRate = 1f,
            float edgeResistanceScale = 1f, float decayRate = 0f)
        {
            var profile = new FieldProfile("test") {
                PropagationRate = propagationRate,
                EdgeResistanceScale = edgeResistanceScale,
                DecayRate = decayRate
            };
            return dim.AddField("test", profile);
        }

        // ─── Guard rails ─────────────────────────────────────────────────────

        [Test]
        public void Step_ZeroDeltaTime_NoChange()
        {
            var dim = MakeLinearChain(new[] { "a", "b" });
            var field = MakeField(dim);
            field.SetLogAmp("a", "ch", 5f);
            float before = field.GetLogAmp("b", "ch");

            Propagator.Step(dim, field, 0f);

            Assert.AreEqual(before, field.GetLogAmp("b", "ch"), 1e-9f);
        }

        [Test]
        public void Step_NoActiveChannels_NoException()
        {
            var dim = MakeLinearChain(new[] { "a", "b" });
            var field = MakeField(dim);
            Assert.DoesNotThrow(() => Propagator.Step(dim, field, 1f));
        }

        [Test]
        public void Step_DisconnectedNodes_NoTransmission()
        {
            var dim = new Dimension();
            dim.AddNode("island");
            dim.AddNode("mainland");
            // No edge between them
            var field = MakeField(dim);
            field.SetLogAmp("island", "ch", 3f);

            Propagator.Step(dim, field, 1f);

            Assert.AreEqual(0f, field.GetLogAmp("mainland", "ch"), 1e-9f);
        }

        // ─── Transmission formula ────────────────────────────────────────────

        [Test]
        public void TransmissionFormula_ZeroResistance_ZeroResistanceScale_FullRate()
        {
            // delta = src × exp(0 × 0) × 1 × 1 = src
            var dim = MakeLinearChain(new[] { "src", "dst" }, resistance: 0f);
            var field = MakeField(dim, propagationRate: 1f, edgeResistanceScale: 0f);
            field.SetLogAmp("src", "ch", 2f);

            Propagator.Step(dim, field, 1f);

            // dst should receive exactly the source logAmp
            Assert.AreEqual(2f, field.GetLogAmp("dst", "ch"), 1e-5f);
        }

        [Test]
        public void TransmissionFormula_ExplicitMathCheck()
        {
            // delta = 3.0 × exp(-2.0 × 1.0) × 0.5 × 1.0
            float srcLogAmp = 3f;
            float resistance = 2f;
            float edgeResistanceScale = 1f;
            float propagationRate = 0.5f;
            float dt = 1f;

            float expected = srcLogAmp * MathF.Exp(-resistance * edgeResistanceScale)
                             * propagationRate * dt;

            var dim = MakeLinearChain(new[] { "src", "dst" }, resistance);
            var field = MakeField(dim, propagationRate: propagationRate,
                edgeResistanceScale: edgeResistanceScale);
            field.SetLogAmp("src", "ch", srcLogAmp);

            Propagator.Step(dim, field, dt);

            Assert.AreEqual(expected, field.GetLogAmp("dst", "ch"), 1e-5f);
        }

        [Test]
        public void TransmissionFormula_DeltaTimeScalesLinearly()
        {
            // Double dt → double transmission
            float srcLogAmp = 2f;
            float resistance = 0f;
            float edgeResistanceScale = 1f;
            float propagationRate = 1f;

            var dim1 = MakeLinearChain(new[] { "s", "d" }, resistance);
            var f1 = MakeField(dim1, propagationRate, edgeResistanceScale);
            f1.SetLogAmp("s", "ch", srcLogAmp);
            Propagator.Step(dim1, f1, 0.1f);

            var dim2 = MakeLinearChain(new[] { "s", "d" }, resistance);
            var f2 = MakeField(dim2, propagationRate, edgeResistanceScale);
            f2.SetLogAmp("s", "ch", srcLogAmp);
            Propagator.Step(dim2, f2, 0.2f);

            float ratio = f2.GetLogAmp("d", "ch") / f1.GetLogAmp("d", "ch");
            Assert.AreEqual(2f, ratio, 1e-4f, "Doubling dt must double delta");
        }

        [Test]
        public void TransmissionFormula_HighResistance_AttenuatesSignal()
        {
            // Resistance=10 → exp(-10) ≈ 4.5e-5 — nearly nothing gets through
            var dim = MakeLinearChain(new[] { "src", "dst" }, resistance: 10f);
            var field = MakeField(dim, propagationRate: 1f, edgeResistanceScale: 1f);
            field.SetLogAmp("src", "ch", 5f);

            Propagator.Step(dim, field, 1f);

            float expected = 5f * MathF.Exp(-10f);
            Assert.AreEqual(expected, field.GetLogAmp("dst", "ch"), 1e-5f);
        }

        [Test]
        public void TransmissionFormula_PropagationRateScalesTransmission()
        {
            var dim1 = MakeLinearChain(new[] { "s", "d" });
            var f1 = MakeField(dim1, propagationRate: 0.25f);
            f1.SetLogAmp("s", "ch", 4f);
            Propagator.Step(dim1, f1, 1f);

            var dim2 = MakeLinearChain(new[] { "s", "d" });
            var f2 = MakeField(dim2, propagationRate: 1f);
            f2.SetLogAmp("s", "ch", 4f);
            Propagator.Step(dim2, f2, 1f);

            Assert.AreEqual(f1.GetLogAmp("d", "ch") * 4f, f2.GetLogAmp("d", "ch"), 1e-4f);
        }

        [Test]
        public void TransmissionFormula_EdgeResistanceScale_ScalesResistanceExponent()
        {
            // scale=0.5 → exp(-R × 0.5) vs scale=1.0 → exp(-R × 1.0)
            float src = 2f;
            float R = 2f;

            var dim1 = MakeLinearChain(new[] { "s", "d" }, R);
            var f1 = MakeField(dim1, edgeResistanceScale: 0.5f);
            f1.SetLogAmp("s", "ch", src);
            Propagator.Step(dim1, f1, 1f);

            var dim2 = MakeLinearChain(new[] { "s", "d" }, R);
            var f2 = MakeField(dim2, edgeResistanceScale: 1f);
            f2.SetLogAmp("s", "ch", src);
            Propagator.Step(dim2, f2, 1f);

            float expected1 = src * MathF.Exp(-R * 0.5f);
            float expected2 = src * MathF.Exp(-R * 1f);

            Assert.AreEqual(expected1, f1.GetLogAmp("d", "ch"), 1e-5f);
            Assert.AreEqual(expected2, f2.GetLogAmp("d", "ch"), 1e-5f);
        }

        [Test]
        public void NegativeSourceLogAmp_PropagatesNegativeDelta()
        {
            // Negative logAmp (scarcity) should propagate as negative delta
            var dim = MakeLinearChain(new[] { "src", "dst" });
            var field = MakeField(dim);
            field.SetLogAmp("src", "ch", -2f);

            Propagator.Step(dim, field, 1f);

            Assert.Less(field.GetLogAmp("dst", "ch"), 0f,
                "Negative source logAmp must produce negative delta at destination");
        }

        // ─── Double-buffer proof ─────────────────────────────────────────────

        [Test]
        public void DoubleBuffer_OneStep_DestinationDoesNotFeedBack()
        {
            // A → B; one Step should not let B feed back into itself via A's update
            var dim = MakeLinearChain(new[] { "a", "b" });
            var field = MakeField(dim);
            field.SetLogAmp("a", "ch", 4f);

            float aBeforeStep = field.GetLogAmp("a", "ch");
            Propagator.Step(dim, field, 1f);

            // A should only have changed by its own decay (none here), not B's contribution back
            // B should have received from A's original value, not a partially-updated A
            float bAfterStep = field.GetLogAmp("b", "ch");
            float expected = 4f * MathF.Exp(0f) * 1f * 1f; // R=0, rate=1, dt=1
            Assert.AreEqual(expected, bAfterStep, 1e-5f);
        }

        [Test]
        public void DoubleBuffer_Chain_A_B_C_OneStep_C_Unaffected()
        {
            // A → B → C with one step: A's logAmp propagates to B,
            // but B starts at 0 so C should receive nothing in the SAME step.
            var dim = MakeLinearChain(new[] { "a", "b", "c" });
            var field = MakeField(dim);
            field.SetLogAmp("a", "ch", 5f);

            Propagator.Step(dim, field, 1f);

            Assert.Greater(field.GetLogAmp("b", "ch"), 0f, "B should have received from A");
            Assert.AreEqual(0f, field.GetLogAmp("c", "ch"), 1e-9f,
                "C must NOT be affected in a single step (double-buffer)");
        }

        [Test]
        public void DoubleBuffer_Chain_A_B_C_TwoSteps_C_Receives()
        {
            var dim = MakeLinearChain(new[] { "a", "b", "c" });
            var field = MakeField(dim);
            field.SetLogAmp("a", "ch", 5f);

            Propagator.Step(dim, field, 1f); // step 1: A→B
            Propagator.Step(dim, field, 1f); // step 2: B→C

            Assert.Greater(field.GetLogAmp("c", "ch"), 0f,
                "After two steps, C must have received signal from B");
        }

        [Test]
        public void DoubleBuffer_SourceLogAmpUsedIsPreStepValue()
        {
            // Two sources feeding one destination.
            // Neither source should see mid-step updates from the other source via the destination.
            var dim = new Dimension();
            dim.AddNode("src1");
            dim.AddNode("src2");
            dim.AddNode("dst");
            dim.AddEdge("src1", "dst", 0f);
            dim.AddEdge("src2", "dst", 0f);

            var field = MakeField(dim);
            field.SetLogAmp("src1", "ch", 2f);
            field.SetLogAmp("src2", "ch", 3f);

            Propagator.Step(dim, field, 1f);

            // dst receives sum of both contributions
            float expected = 2f + 3f; // both transmit with zero resistance
            Assert.AreEqual(expected, field.GetLogAmp("dst", "ch"), 1e-5f);
        }

        // ─── Multiple sources accumulate ─────────────────────────────────────

        [Test]
        public void MultipleSourcesToOneTarget_ContributionsAdd()
        {
            var dim = new Dimension();
            dim.AddNode("a"); dim.AddNode("b"); dim.AddNode("target");
            dim.AddEdge("a", "target", 0f);
            dim.AddEdge("b", "target", 0f);

            var field = MakeField(dim);
            field.SetLogAmp("a", "ch", 1f);
            field.SetLogAmp("b", "ch", 1f);

            Propagator.Step(dim, field, 1f);

            Assert.AreEqual(2f, field.GetLogAmp("target", "ch"), 1e-5f);
        }

        // ─── Edge tag filtering ──────────────────────────────────────────────

        [Test]
        public void EdgeTagFilter_RequiredTag_OnlyMatchingEdgesTraverse()
        {
            var dim = new Dimension();
            dim.AddNode("src");
            dim.AddNode("sea-dst");
            dim.AddNode("land-dst");

            dim.AddEdge("src", "sea-dst", 0f, "sea");
            dim.AddEdge("src", "land-dst", 0f, "land");

            var field = MakeField(dim);
            field.SetLogAmp("src", "ch", 3f);

            Propagator.Step(dim, field, 1f, requiredEdgeTag: "sea");

            Assert.Greater(field.GetLogAmp("sea-dst", "ch"), 0f, "sea edge should transmit");
            Assert.AreEqual(0f, field.GetLogAmp("land-dst", "ch"), 1e-9f,
                "land edge must be blocked when filtering for sea");
        }

        [Test]
        public void EdgeTagFilter_NoFilter_AllEdgesTraverse()
        {
            var dim = new Dimension();
            dim.AddNode("src");
            dim.AddNode("a"); dim.AddNode("b");
            dim.AddEdge("src", "a", 0f, "sea");
            dim.AddEdge("src", "b", 0f, "land");

            var field = MakeField(dim);
            field.SetLogAmp("src", "ch", 2f);

            Propagator.Step(dim, field, 1f, requiredEdgeTag: null);

            Assert.Greater(field.GetLogAmp("a", "ch"), 0f);
            Assert.Greater(field.GetLogAmp("b", "ch"), 0f);
        }

        [Test]
        public void EdgeTagFilter_NoEdgeHasTag_NothingTraverses()
        {
            var dim = new Dimension();
            dim.AddNode("src"); dim.AddNode("dst");
            dim.AddEdge("src", "dst", 0f, "land"); // has "land", not "sea"

            var field = MakeField(dim);
            field.SetLogAmp("src", "ch", 5f);

            Propagator.Step(dim, field, 1f, requiredEdgeTag: "sea");

            Assert.AreEqual(0f, field.GetLogAmp("dst", "ch"), 1e-9f);
        }

        [Test]
        public void EdgeTagFilter_EdgeWithMultipleTags_MatchesOnAny()
        {
            var dim = new Dimension();
            dim.AddNode("src"); dim.AddNode("dst");
            dim.AddEdge("src", "dst", 0f, "sea", "trade");

            var field = MakeField(dim);
            field.SetLogAmp("src", "ch", 2f);

            Propagator.Step(dim, field, 1f, requiredEdgeTag: "trade");

            Assert.Greater(field.GetLogAmp("dst", "ch"), 0f);
        }

        // ─── Multiple channels propagate independently ────────────────────────

        [Test]
        public void MultipleChannels_PropagateIndependently()
        {
            var dim = MakeLinearChain(new[] { "src", "dst" });
            var field = MakeField(dim);
            field.SetLogAmp("src", "water", 2f);
            field.SetLogAmp("src", "ore", 1f);

            Propagator.Step(dim, field, 1f);

            Assert.Greater(field.GetLogAmp("dst", "water"), 0f);
            Assert.Greater(field.GetLogAmp("dst", "ore"), 0f);

            // Ratio should reflect source ratio (resistance=0, rate=1)
            Assert.AreEqual(
                field.GetLogAmp("src", "water") / field.GetLogAmp("src", "ore"),
                field.GetLogAmp("dst", "water") / field.GetLogAmp("dst", "ore"),
                1e-4f, "Channel ratios must be preserved");
        }

        [Test]
        public void UnrelatedChannel_Unaffected()
        {
            var dim = MakeLinearChain(new[] { "src", "dst" });
            var field = MakeField(dim);
            field.SetLogAmp("src", "active-ch", 3f);
            field.SetLogAmp("src", "quiet-ch", 0f); // remains neutral

            Propagator.Step(dim, field, 1f);

            Assert.AreEqual(0f, field.GetLogAmp("dst", "quiet-ch"), 1e-9f,
                "Neutral channel must not propagate");
        }

        // ─── Decay ───────────────────────────────────────────────────────────

        [Test]
        public void DecayRate_Zero_SourceUnchanged()
        {
            var dim = MakeLinearChain(new[] { "a", "b" });
            var field = MakeField(dim, decayRate: 0f);
            field.SetLogAmp("a", "ch", 4f);

            Propagator.Step(dim, field, 1f);

            Assert.AreEqual(4f, field.GetLogAmp("a", "ch"), 1e-5f,
                "With zero decay, source amplitude must be unchanged");
        }

        [Test]
        public void DecayRate_Positive_ReducesSourceAmplitude()
        {
            var dim = new Dimension();
            dim.AddNode("a"); // isolated, no edges
            var field = MakeField(dim, decayRate: 0.5f);
            field.SetLogAmp("a", "ch", 4f);

            Propagator.Step(dim, field, 1f);

            Assert.Less(field.GetLogAmp("a", "ch"), 4f,
                "Positive decay must reduce source logAmp over time");
        }

        [Test]
        public void DecayRate_HighEnough_EventuallyReachesNeutral()
        {
            var dim = new Dimension();
            dim.AddNode("node");
            var field = MakeField(dim, decayRate: 5f); // very high decay

            field.SetLogAmp("node", "ch", 3f);

            for (int i = 0; i < 100; i++)
                Propagator.Step(dim, field, 0.1f);

            Assert.AreEqual(0f, field.GetLogAmp("node", "ch"), 0.01f,
                "High decay must bring amplitude to neutral over time");
        }

        // ─── Order independence ───────────────────────────────────────────────

        [Test]
        public void OrderIndependence_NodeInsertionOrder_DoesNotAffectResult()
        {
            // Two equivalent graphs with nodes inserted in different order
            string[] orderA = { "src", "n1", "n2" };
            string[] orderB = { "n2", "src", "n1" };

            var dim1 = new Dimension();
            foreach (var id in orderA) dim1.AddNode(id);
            dim1.AddEdge("src", "n1", 0f);
            dim1.AddEdge("src", "n2", 0f);
            var f1 = MakeField(dim1);
            f1.SetLogAmp("src", "ch", 3f);

            var dim2 = new Dimension();
            foreach (var id in orderB) dim2.AddNode(id);
            dim2.AddEdge("src", "n1", 0f);
            dim2.AddEdge("src", "n2", 0f);
            var f2 = MakeField(dim2);
            f2.SetLogAmp("src", "ch", 3f);

            Propagator.Step(dim1, f1, 1f);
            Propagator.Step(dim2, f2, 1f);

            Assert.AreEqual(f1.GetLogAmp("n1", "ch"), f2.GetLogAmp("n1", "ch"), 1e-6f);
            Assert.AreEqual(f1.GetLogAmp("n2", "ch"), f2.GetLogAmp("n2", "ch"), 1e-6f);
        }

        [Test]
        public void OrderIndependence_ChannelInjectionOrder_DoesNotAffectResult()
        {
            var dim1 = MakeLinearChain(new[] { "a", "b" });
            var f1 = MakeField(dim1);
            f1.SetLogAmp("a", "ch1", 1f);
            f1.SetLogAmp("a", "ch2", 2f);

            var dim2 = MakeLinearChain(new[] { "a", "b" });
            var f2 = MakeField(dim2);
            f2.SetLogAmp("a", "ch2", 2f);
            f2.SetLogAmp("a", "ch1", 1f);

            Propagator.Step(dim1, f1, 1f);
            Propagator.Step(dim2, f2, 1f);

            Assert.AreEqual(f1.GetLogAmp("b", "ch1"), f2.GetLogAmp("b", "ch1"), 1e-6f);
            Assert.AreEqual(f1.GetLogAmp("b", "ch2"), f2.GetLogAmp("b", "ch2"), 1e-6f);
        }

        // ─── Star graph ───────────────────────────────────────────────────────

        [Test]
        public void StarGraph_HubSource_AllLeafNodesReceive()
        {
            int leafCount = 5;
            var dim = new Dimension();
            dim.AddNode("hub");
            for (int i = 0; i < leafCount; i++)
            {
                dim.AddNode($"leaf{i}");
                dim.AddEdge("hub", $"leaf{i}", 0f);
            }

            var field = MakeField(dim);
            field.SetLogAmp("hub", "ch", 2f);

            Propagator.Step(dim, field, 1f);

            for (int i = 0; i < leafCount; i++)
                Assert.AreEqual(2f, field.GetLogAmp($"leaf{i}", "ch"), 1e-5f,
                    $"leaf{i} must receive from hub");
        }

        [Test]
        public void StarGraph_LeafSource_HubReceives()
        {
            var dim = new Dimension();
            dim.AddNode("hub");
            dim.AddNode("leaf");
            dim.AddEdge("leaf", "hub", 0f);

            var field = MakeField(dim);
            field.SetLogAmp("leaf", "ch", 3f);

            Propagator.Step(dim, field, 1f);

            Assert.AreEqual(3f, field.GetLogAmp("hub", "ch"), 1e-5f);
        }

        // ─── Tiny dt ─────────────────────────────────────────────────────────

        [Test]
        public void TinyDeltaTime_ProducesSmallButNonZeroDelta()
        {
            var dim = MakeLinearChain(new[] { "src", "dst" });
            var field = MakeField(dim);
            field.SetLogAmp("src", "ch", 10f);

            Propagator.Step(dim, field, 0.001f);

            float received = field.GetLogAmp("dst", "ch");
            float expected = 10f * 0.001f;
            Assert.AreEqual(expected, received, 1e-5f);
        }
    }
}
