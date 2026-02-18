using NUnit.Framework;
using Odengine.Core;
using Odengine.Graph;
using Odengine.Fields;
using System.Collections.Generic;

namespace Odengine.Tests
{
    [TestFixture]
    public class CoreTests
    {
        [Test]
        public void NodeGraph_AddNode_Works()
        {
            var graph = new NodeGraph();
            var node = new Node("planet1", "Planet Alpha");

            graph.AddNode(node);

            Assert.That(graph.TryGetNode("planet1", out var retrieved), Is.True);
            Assert.That(retrieved.Id, Is.EqualTo("planet1"));
        }

        [Test]
        public void Field_StoresAmplitudePerNode()
        {
            var profile = new FieldProfile("test_profile", 0.5f, 0.01f, 1.0f, 0.0f);
            var field = new Field("availability", profile);

            field.SetAmplitude("node1", 0.5f);
            field.SetAmplitude("node2", 0.8f);

            Assert.That(field.GetAmplitude("node1"), Is.EqualTo(0.5f));
            Assert.That(field.GetAmplitude("node2"), Is.EqualTo(0.8f));
            Assert.That(field.GetAmplitude("node3"), Is.EqualTo(0f)); // default
        }

        [Test]
        public void FieldSampler_SamplesAmplitude_Deterministically()
        {
            var graph = new NodeGraph();
            var node1 = new Node("node1", "Node 1");
            var node2 = new Node("node2", "Node 2");
            graph.AddNode(node1);
            graph.AddNode(node2);

            var profile = new FieldProfile("price_profile", 0.5f, 0.01f, 1.0f, 0.0f);
            var field = new Field("price", profile);
            field.SetAmplitude("node1", 0.7f);
            field.SetAmplitude("node2", 0.3f);

            var sampler = new DirectAmplitudeSampler("test");
            var price1 = (float)sampler.Sample(node1, field);
            var price2 = (float)sampler.Sample(node2, field);

            Assert.That(price1, Is.GreaterThan(price2)); // higher amp = higher sample

            // Determinism: same inputs = same output
            var price1Again = (float)sampler.Sample(node1, field);
            Assert.That(price1Again, Is.EqualTo(price1));
        }

        [Test]
        public void FieldPropagation_AttenuatesWithResistance()
        {
            var graph = new NodeGraph();
            var nodeA = new Node("a", "A");
            var nodeB = new Node("b", "B");
            graph.AddNode(nodeA);
            graph.AddNode(nodeB);
            graph.AddEdge("a", "b", 50f); // moderate resistance

            var profile = new FieldProfile("power_profile", 0.5f, 0.0f, 1.0f, 0.0f);
            var field = new Field("power", profile);
            field.SetAmplitude("a", 1.0f);
            field.SetAmplitude("b", 0.0f);

            var deltas = FieldPropagator.Step(field, graph, 1.0f);
            field.ApplyDeltas(deltas);

            var ampB = field.GetAmplitude("b");
            Assert.That(ampB, Is.GreaterThan(0f)); // some propagation
            Assert.That(ampB, Is.LessThan(1.0f)); // but attenuated
        }

        [Test]
        public void Dimension_IntegratesAllComponents()
        {
            var world = new Dimension();
            var node = new Node("node1", "Node 1");
            world.Graph.AddNode(node);

            var profile = new FieldProfile("availability_profile", 0.5f, 0.01f, 1.0f, 0.0f);
            var field = world.AddField("availability", profile);

            field.SetAmplitude("node1", 0.75f);

            Assert.That(world.GetField("availability"), Is.Not.Null);
            Assert.That(field.GetAmplitude("node1"), Is.EqualTo(0.75f));
        }
    }

    /// <summary>
    /// Simple test sampler that returns amplitude directly
    /// </summary>
    public class DirectAmplitudeSampler : FieldSampler
    {
        public DirectAmplitudeSampler(string id) : base(id) { }

        public override object Sample(Node node, Field field, Dictionary<string, object> context = null)
        {
            return field.GetAmplitude(node.Id);
        }
    }
}
