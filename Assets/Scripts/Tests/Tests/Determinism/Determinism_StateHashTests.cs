using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;
using Odengine.Tests.Shared;

namespace Odengine.Tests.Determinism
{
    /// <summary>
    /// Tests for <see cref="StateHash"/> correctness and determinism invariants.
    ///
    /// Key invariant: given the same logical state (same nodes, edges, field amplitudes)
    /// the hash must be identical regardless of insertion order, re-run, or parallel execution.
    /// </summary>
    [TestFixture]
    public class Determinism_StateHashTests
    {
        private static FieldProfile DefaultProfile(string id = "f") =>
            new FieldProfile(id) { PropagationRate = 1f, DecayRate = 0f };

        // ─── Stability ────────────────────────────────────────────────────────

        [Test]
        public void EmptyDimension_HashIsStableAcrossCallsOnSameInstance()
        {
            var dim = new Dimension();
            string h1 = StateHash.Compute(dim);
            string h2 = StateHash.Compute(dim);
            Assert.AreEqual(h1, h2);
        }

        [Test]
        public void EmptyDimension_TwoSeparateInstances_SameHash()
        {
            var a = new Dimension();
            var b = new Dimension();
            Assert.AreEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void HashIsHexString_NonEmpty()
        {
            var dim = new Dimension();
            string h = StateHash.Compute(dim);
            Assert.IsNotNull(h);
            Assert.IsNotEmpty(h);
        }

        // ─── Graph topology affects hash ──────────────────────────────────────

        [Test]
        public void SameNodes_SameHash()
        {
            var a = new Dimension(); a.AddNode("x"); a.AddNode("y");
            var b = new Dimension(); b.AddNode("x"); b.AddNode("y");
            Assert.AreEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void DifferentNodes_DifferentHash()
        {
            var a = new Dimension(); a.AddNode("x");
            var b = new Dimension(); b.AddNode("y");
            Assert.AreNotEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void NodeInsertionOrder_DoesNotAffectHash()
        {
            var a = new Dimension();
            a.AddNode("c"); a.AddNode("a"); a.AddNode("b");

            var b = new Dimension();
            b.AddNode("a"); b.AddNode("b"); b.AddNode("c");

            Assert.AreEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void AdditionalNode_ChangesHash()
        {
            var a = new Dimension(); a.AddNode("x");
            var b = new Dimension(); b.AddNode("x"); b.AddNode("extra");
            Assert.AreNotEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void SameEdges_SameHash()
        {
            Dimension Make()
            {
                var d = new Dimension();
                d.AddNode("a"); d.AddNode("b");
                d.AddEdge("a", "b", 2f, "sea");
                return d;
            }
            Assert.AreEqual(StateHash.Compute(Make()), StateHash.Compute(Make()));
        }

        [Test]
        public void DifferentEdgeResistance_DifferentHash()
        {
            var a = new Dimension(); a.AddNode("x"); a.AddNode("y"); a.AddEdge("x", "y", 1f);
            var b = new Dimension(); b.AddNode("x"); b.AddNode("y"); b.AddEdge("x", "y", 2f);
            Assert.AreNotEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void DifferentEdgeTags_DifferentHash()
        {
            var a = new Dimension();
            a.AddNode("x"); a.AddNode("y");
            a.AddEdge("x", "y", 1f, "sea");

            var b = new Dimension();
            b.AddNode("x"); b.AddNode("y");
            b.AddEdge("x", "y", 1f, "land");

            Assert.AreNotEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        // ─── Field amplitudes affect hash ─────────────────────────────────────

        [Test]
        public void SameFieldValues_SameHash()
        {
            Dimension Make()
            {
                var d = new Dimension();
                d.AddNode("n");
                var f = d.AddField("test", DefaultProfile());
                f.SetLogAmp("n", "ch", 2.5f);
                return d;
            }
            Assert.AreEqual(StateHash.Compute(Make()), StateHash.Compute(Make()));
        }

        [Test]
        public void DifferentFieldValues_DifferentHash()
        {
            var a = new Dimension(); a.AddNode("n");
            a.AddField("f", DefaultProfile()).SetLogAmp("n", "ch", 1f);

            var b = new Dimension(); b.AddNode("n");
            b.AddField("f", DefaultProfile()).SetLogAmp("n", "ch", 2f);

            Assert.AreNotEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void NeutralValue_SameAsNoValue_SameHash()
        {
            // Setting a logAmp that gets pruned (below epsilon) = same as not setting it
            var a = new Dimension(); a.AddNode("n");
            a.AddField("f", DefaultProfile());

            var b = new Dimension(); b.AddNode("n");
            var bf = b.AddField("f", DefaultProfile());
            bf.SetLogAmp("n", "ch", 0f); // immediately pruned

            Assert.AreEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void InjectionOrder_SameState_SameHash()
        {
            Dimension Make(float v1, float v2)
            {
                var d = new Dimension(); d.AddNode("n");
                var f = d.AddField("g", DefaultProfile());
                f.AddLogAmp("n", "ch", v1);
                f.AddLogAmp("n", "ch", v2);
                return d;
            }

            // Same total logAmp either way (addition is commutative)
            Assert.AreEqual(StateHash.Compute(Make(1f, 2f)), StateHash.Compute(Make(2f, 1f)));
        }

        [Test]
        public void DifferentFieldIds_DifferentHash()
        {
            var a = new Dimension(); a.AddNode("n");
            a.AddField("field-alpha", DefaultProfile("field-alpha")).SetLogAmp("n", "ch", 1f);

            var b = new Dimension(); b.AddNode("n");
            b.AddField("field-beta", DefaultProfile("field-beta")).SetLogAmp("n", "ch", 1f);

            Assert.AreNotEqual(StateHash.Compute(a), StateHash.Compute(b));
        }

        [Test]
        public void ChannelOrder_DoesNotAffectHash()
        {
            // Same channel→value mapping, different insertion order → identical hash
            var d1 = new Dimension(); d1.AddNode("n");
            var f1 = d1.AddField("g", DefaultProfile());
            f1.SetLogAmp("n", "a", 1f);
            f1.SetLogAmp("n", "b", 2f);  // a first, then b

            var d2 = new Dimension(); d2.AddNode("n");
            var f2 = d2.AddField("g", DefaultProfile());
            f2.SetLogAmp("n", "b", 2f);  // b first
            f2.SetLogAmp("n", "a", 1f);  // then a — same content, different insertion order

            Assert.AreEqual(StateHash.Compute(d1), StateHash.Compute(d2));
        }

        // ─── Propagation determinism ──────────────────────────────────────────

        [Test]
        public void PropagationStep_ProducesSameHashAsIdenticalRun()
        {
            Dimension MakeAndPropagate()
            {
                var d = new Dimension();
                d.AddNode("a"); d.AddNode("b");
                d.AddEdge("a", "b", 1f);
                var f = d.AddField("g", DefaultProfile());
                f.SetLogAmp("a", "ch", 3f);
                Propagator.Step(d, f, 0.5f);
                return d;
            }

            Assert.AreEqual(
                StateHash.Compute(MakeAndPropagate()),
                StateHash.Compute(MakeAndPropagate()));
        }

        [Test]
        public void TenPropagationSteps_HashIsStableAcrossTwoIdenticalRuns()
        {
            Dimension Run()
            {
                var d = new Dimension();
                d.AddNode("x"); d.AddNode("y"); d.AddNode("z");
                d.AddEdge("x", "y", 0.5f);
                d.AddEdge("y", "z", 0.5f);
                var f = d.AddField("g", DefaultProfile());
                f.SetLogAmp("x", "ch", 5f);
                for (int i = 0; i < 10; i++) Propagator.Step(d, f, 0.1f);
                return d;
            }

            Assert.AreEqual(StateHash.Compute(Run()), StateHash.Compute(Run()));
        }

        [Test]
        public void ReplayImpulses_ProducesSameHash()
        {
            Dimension Build()
            {
                var d = new Dimension();
                d.AddNode("n1"); d.AddNode("n2");
                d.AddEdge("n1", "n2", 1f);
                var f = d.AddField("g", DefaultProfile());
                f.AddLogAmp("n1", "trade", 0.5f);
                f.AddLogAmp("n1", "trade", 0.5f); // two identical impulses
                return d;
            }

            Assert.AreEqual(StateHash.Compute(Build()), StateHash.Compute(Build()));
        }

        // ─── Large amplitude stability ────────────────────────────────────────

        [Test]
        public void LargeAmplitude_NearClampBounds_HashIsStable()
        {
            Dimension Make()
            {
                var d = new Dimension(); d.AddNode("n");
                d.AddField("g", DefaultProfile()).SetLogAmp("n", "ch", 19f);
                return d;
            }
            Assert.AreEqual(StateHash.Compute(Make()), StateHash.Compute(Make()));
        }

        // ─── Two distinct states never collide ───────────────────────────────

        [Test]
        public void TwentyDistinctStates_AllHashesDifferent()
        {
            var hashes = new HashSet<string>();
            for (int i = 0; i < 20; i++)
            {
                var d = new Dimension();
                d.AddNode($"n{i}");
                d.AddField("g", DefaultProfile()).SetLogAmp($"n{i}", "ch", (i + 1) * 0.5f);
                hashes.Add(StateHash.Compute(d));
            }
            // All hashes must be unique
            Assert.AreEqual(20, hashes.Count, "Each distinct state must produce a unique hash");
        }
    }
}
