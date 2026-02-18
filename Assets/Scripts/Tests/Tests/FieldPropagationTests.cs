using NUnit.Framework;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Tests
{
    [TestFixture]
    public class FieldPropagationTests
    {
        [Test]
        public void NeutralBaseline_IsOneEverywhere()
        {
            var dimension = new Dimension();
            dimension.AddNode("node1");
            dimension.AddNode("node2");

            var profile = new FieldProfile("test");
            var field = dimension.AddField("test_field", profile);

            // No data stored → neutral baseline = 1.0
            Assert.AreEqual(1.0f, field.GetMultiplier("node1", "water"), 0.0001f);
            Assert.AreEqual(1.0f, field.GetMultiplier("node2", "gold"), 0.0001f);
            Assert.AreEqual(1.0f, field.GetMultiplier("nonexistent", "anything"), 0.0001f);
        }

        [Test]
        public void Propagation_OrderIndependent()
        {
            // Build graph A->B, A->C
            var dim1 = CreateTestDimension("A", "B", "C");
            var dim2 = CreateTestDimension("C", "B", "A"); // Reversed insertion order

            // Verify edges exist
            var edgesFromA1 = dim1.Graph.GetOutEdgesSorted("A");
            UnityEngine.Debug.Log($"Dim1: A has {edgesFromA1.Count} edges");
            var edgesFromA2 = dim2.Graph.GetOutEdgesSorted("A");
            UnityEngine.Debug.Log($"Dim2: A has {edgesFromA2.Count} edges");

            var profile = new FieldProfile("test") { PropagationRate = 0.5f, EdgeResistanceScale = 0.1f };
            var field1 = dim1.AddField("test", profile);
            var field2 = dim2.AddField("test", profile);

            // Set same initial condition
            field1.SetLogAmp("A", "water", 2.0f);
            field2.SetLogAmp("A", "water", 2.0f);

            UnityEngine.Debug.Log($"Field1 initial logAmp at A: {field1.GetLogAmp("A", "water")}");
            UnityEngine.Debug.Log($"Field2 initial logAmp at A: {field2.GetLogAmp("A", "water")}");

            // Propagate
            Propagator.Step(dim1, field1, 1.0f);
            Propagator.Step(dim2, field2, 1.0f);

            UnityEngine.Debug.Log($"Field1 after prop - B: {field1.GetMultiplier("B", "water")}, C: {field1.GetMultiplier("C", "water")}");
            UnityEngine.Debug.Log($"Field2 after prop - B: {field2.GetMultiplier("B", "water")}, C: {field2.GetMultiplier("C", "water")}");

            // Results must match exactly
            float mult1B = field1.GetMultiplier("B", "water");
            float mult2B = field2.GetMultiplier("B", "water");
            float mult1C = field1.GetMultiplier("C", "water");
            float mult2C = field2.GetMultiplier("C", "water");

            Assert.AreEqual(mult1B, mult2B, 0.0001f, "B values must match regardless of node insertion order");
            Assert.AreEqual(mult1C, mult2C, 0.0001f, "C values must match regardless of node insertion order");
        }

        [Test]
        public void ResistanceBlocksTransmission()
        {
            var dimension = new Dimension();
            dimension.AddNode("source");
            dimension.AddNode("lowR");
            dimension.AddNode("highR");

            dimension.AddEdge("source", "lowR", 0.1f);
            dimension.AddEdge("source", "highR", 5.0f);

            var profile = new FieldProfile("test") { PropagationRate = 1f, EdgeResistanceScale = 1f };
            var field = dimension.AddField("test", profile);

            field.SetLogAmp("source", "water", 3.0f);

            Propagator.Step(dimension, field, 1.0f);

            float multLowR = field.GetMultiplier("lowR", "water");
            float multHighR = field.GetMultiplier("highR", "water");

            Assert.Greater(multLowR, multHighR, "Low resistance path should receive more amplitude");
        }

        [Test]
        public void EdgeTags_FilterWorks()
        {
            var dimension = new Dimension();
            dimension.AddNode("A");
            dimension.AddNode("B");
            dimension.AddNode("C");

            dimension.AddEdge("A", "B", 1.0f, "land");
            dimension.AddEdge("A", "C", 1.0f, "sea");

            var profile = new FieldProfile("test") { PropagationRate = 1f, EdgeResistanceScale = 0.1f };
            var field = dimension.AddField("test", profile);

            field.SetLogAmp("A", "armies", 2.0f);

            // Propagate only over "land" edges
            Propagator.Step(dimension, field, 1.0f, requiredEdgeTag: "land");

            float multB = field.GetMultiplier("B", "armies");
            float multC = field.GetMultiplier("C", "armies");

            Assert.Greater(multB, 1.0f, "B (land) should receive amplitude");
            Assert.AreEqual(1.0f, multC, 0.0001f, "C (sea) should not receive amplitude");
        }

        private Dimension CreateTestDimension(params string[] nodeIds)
        {
            var dim = new Dimension();
            foreach (var id in nodeIds)
                dim.AddNode(id);

            // Always add edges A->B, A->C (regardless of insertion order)
            dim.AddEdge("A", "B", 1.0f);
            dim.AddEdge("A", "C", 1.0f);

            return dim;
        }
    }
}
