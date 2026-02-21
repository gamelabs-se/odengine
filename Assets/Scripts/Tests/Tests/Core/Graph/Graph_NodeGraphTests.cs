using System;
using System.Linq;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Graph;

namespace Odengine.Tests.Core.Graph
{
    [TestFixture]
    public class Graph_NodeGraphTests
    {
        // ── AddNode ─────────────────────────────────────────────────────────

        [Test]
        public void AddNode_NodeIsRetrievable()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("city-a"));
            Assert.IsTrue(graph.TryGetNode("city-a", out _));
        }

        [Test]
        public void AddNode_DuplicateId_Overwrites()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("x", "Old"));
            graph.AddNode(new Node("x", "New"));
            graph.TryGetNode("x", out var node);
            Assert.AreEqual("New", node.Name);
        }

        [Test]
        public void AddNode_NullNode_Throws()
        {
            var graph = new NodeGraph();
            Assert.Throws<ArgumentNullException>(() => graph.AddNode(null));
        }

        [Test]
        public void AddOrUpdateNode_UpdatesName()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("n1", "First"));
            graph.AddOrUpdateNode(new Node("n1", "Second"));
            graph.TryGetNode("n1", out var node);
            Assert.AreEqual("Second", node.Name);
        }

        [Test]
        public void AddOrUpdateNode_PreservesExistingEdges()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("a"));
            graph.AddNode(new Node("b"));
            graph.AddEdge("a", "b", 1f);

            // update node a — edges must survive
            graph.AddOrUpdateNode(new Node("a", "Renamed"));
            var edges = graph.GetOutEdgesSorted("a");
            Assert.AreEqual(1, edges.Count);
        }

        // ── TryGetNode ──────────────────────────────────────────────────────

        [Test]
        public void TryGetNode_ReturnsTrueForExisting()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("x"));
            Assert.IsTrue(graph.TryGetNode("x", out _));
        }

        [Test]
        public void TryGetNode_ReturnsFalseForMissing()
        {
            var graph = new NodeGraph();
            Assert.IsFalse(graph.TryGetNode("missing", out _));
        }

        [Test]
        public void TryGetNode_OutParamIsCorrectNode()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("n", "Label"));
            graph.TryGetNode("n", out var node);
            Assert.AreEqual("Label", node.Name);
        }

        // ── GetNodeIdsSorted ────────────────────────────────────────────────

        [Test]
        public void GetNodeIdsSorted_IsOrdinalSorted()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("z"));
            graph.AddNode(new Node("a"));
            graph.AddNode(new Node("m"));

            var ids = graph.GetNodeIdsSorted();
            Assert.AreEqual("a", ids[0]);
            Assert.AreEqual("m", ids[1]);
            Assert.AreEqual("z", ids[2]);
        }

        [Test]
        public void GetNodeIdsSorted_StableRegardlessOfInsertionOrder()
        {
            var g1 = new NodeGraph();
            g1.AddNode(new Node("c"));
            g1.AddNode(new Node("a"));
            g1.AddNode(new Node("b"));

            var g2 = new NodeGraph();
            g2.AddNode(new Node("b"));
            g2.AddNode(new Node("c"));
            g2.AddNode(new Node("a"));

            var ids1 = g1.GetNodeIdsSorted();
            var ids2 = g2.GetNodeIdsSorted();
            CollectionAssert.AreEqual(ids1, ids2);
        }

        [Test]
        public void GetNodeIdsSorted_EmptyGraph_ReturnsEmpty()
        {
            var graph = new NodeGraph();
            Assert.AreEqual(0, graph.GetNodeIdsSorted().Count);
        }

        // ── AddEdge ─────────────────────────────────────────────────────────

        [Test]
        public void AddEdge_ThrowsIfFromNodeMissing()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("b"));
            Assert.Throws<InvalidOperationException>(() => graph.AddEdge("missing", "b", 1f));
        }

        [Test]
        public void AddEdge_ThrowsIfToNodeMissing()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("a"));
            Assert.Throws<InvalidOperationException>(() => graph.AddEdge("a", "missing", 1f));
        }

        [Test]
        public void AddEdge_ThrowsIfBothNodesMissing()
        {
            var graph = new NodeGraph();
            Assert.Throws<InvalidOperationException>(() => graph.AddEdge("x", "y", 1f));
        }

        [Test]
        public void AddEdge_ZeroResistanceIsValid()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("a"));
            graph.AddNode(new Node("b"));
            Assert.DoesNotThrow(() => graph.AddEdge("a", "b", 0f));
        }

        [Test]
        public void AddEdge_NegativeResistanceThrows()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("a"));
            graph.AddNode(new Node("b"));
            Assert.Throws<ArgumentException>(() => graph.AddEdge("a", "b", -1f));
        }

        [Test]
        public void AddEdge_WithTags_TagsStoredOnEdge()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("a"));
            graph.AddNode(new Node("b"));
            graph.AddEdge("a", "b", 1f, "sea", "trade");

            var edge = graph.GetOutEdgesSorted("a")[0];
            Assert.IsTrue(edge.HasTag("sea"));
            Assert.IsTrue(edge.HasTag("trade"));
        }

        [Test]
        public void AddEdge_WithoutTags_EmptyTagSet()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("a"));
            graph.AddNode(new Node("b"));
            graph.AddEdge("a", "b", 1f);

            var edge = graph.GetOutEdgesSorted("a")[0];
            Assert.AreEqual(0, edge.Tags.Count);
        }

        // ── GetOutEdgesSorted ───────────────────────────────────────────────

        [Test]
        public void GetOutEdgesSorted_ReturnsSortedByToIdOrdinal()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("src"));
            graph.AddNode(new Node("z"));
            graph.AddNode(new Node("a"));
            graph.AddNode(new Node("m"));

            graph.AddEdge("src", "z", 1f);
            graph.AddEdge("src", "a", 1f);
            graph.AddEdge("src", "m", 1f);

            var edges = graph.GetOutEdgesSorted("src");
            Assert.AreEqual("a", edges[0].ToId);
            Assert.AreEqual("m", edges[1].ToId);
            Assert.AreEqual("z", edges[2].ToId);
        }

        [Test]
        public void GetOutEdgesSorted_EmptyForUnknownNode()
        {
            var graph = new NodeGraph();
            var edges = graph.GetOutEdgesSorted("ghost");
            Assert.AreEqual(0, edges.Count);
        }

        [Test]
        public void GetOutEdgesSorted_EmptyForNodeWithNoOutEdges()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("leaf"));
            var edges = graph.GetOutEdgesSorted("leaf");
            Assert.AreEqual(0, edges.Count);
        }

        [Test]
        public void GetOutEdgesSorted_AllEdgesPresent()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("src"));
            graph.AddNode(new Node("a"));
            graph.AddNode(new Node("b"));
            graph.AddNode(new Node("c"));

            graph.AddEdge("src", "a", 1f);
            graph.AddEdge("src", "b", 2f);
            graph.AddEdge("src", "c", 3f);

            var edges = graph.GetOutEdgesSorted("src");
            Assert.AreEqual(3, edges.Count);
        }

        [Test]
        public void GetOutEdgesSorted_MultipleEdgesSameFrom_AllReturned()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("hub"));
            graph.AddNode(new Node("n1"));
            graph.AddNode(new Node("n2"));
            graph.AddEdge("hub", "n1", 1f);
            graph.AddEdge("hub", "n2", 1f);

            Assert.AreEqual(2, graph.GetOutEdgesSorted("hub").Count);
        }

        [Test]
        public void GetOutEdgesSorted_OrderStableRegardlessOfEdgeInsertionOrder()
        {
            // Graph 1: add edges z→a, z→b, z→c in natural order
            var g1 = new NodeGraph();
            foreach (var id in new[] { "hub", "a", "b", "c" })
                g1.AddNode(new Node(id));
            g1.AddEdge("hub", "c", 1f);
            g1.AddEdge("hub", "a", 1f);
            g1.AddEdge("hub", "b", 1f);

            // Graph 2: reversed
            var g2 = new NodeGraph();
            foreach (var id in new[] { "hub", "a", "b", "c" })
                g2.AddNode(new Node(id));
            g2.AddEdge("hub", "a", 1f);
            g2.AddEdge("hub", "b", 1f);
            g2.AddEdge("hub", "c", 1f);

            var e1 = g1.GetOutEdgesSorted("hub").Select(e => e.ToId).ToList();
            var e2 = g2.GetOutEdgesSorted("hub").Select(e => e.ToId).ToList();
            CollectionAssert.AreEqual(e1, e2);
        }

        // ── Determinism ─────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameTopologyDifferentNodeOrder_SameNodeIdsSorted()
        {
            var g1 = new NodeGraph();
            g1.AddNode(new Node("x")); g1.AddNode(new Node("y")); g1.AddNode(new Node("z"));

            var g2 = new NodeGraph();
            g2.AddNode(new Node("z")); g2.AddNode(new Node("x")); g2.AddNode(new Node("y"));

            CollectionAssert.AreEqual(g1.GetNodeIdsSorted(), g2.GetNodeIdsSorted());
        }

        [Test]
        public void Determinism_Nodes_ReferencedViaNodesProperty()
        {
            var graph = new NodeGraph();
            graph.AddNode(new Node("alpha"));
            graph.AddNode(new Node("beta"));

            Assert.IsTrue(graph.Nodes.ContainsKey("alpha"));
            Assert.IsTrue(graph.Nodes.ContainsKey("beta"));
        }

        // ── Dimension convenience API ───────────────────────────────────────

        [Test]
        public void Dimension_AddNode_NodeAvailableInGraph()
        {
            var dim = new Dimension();
            dim.AddNode("city");
            Assert.IsTrue(dim.Graph.TryGetNode("city", out _));
        }

        [Test]
        public void Dimension_AddEdge_RequiresBothNodesExist()
        {
            var dim = new Dimension();
            dim.AddNode("a");
            Assert.Throws<InvalidOperationException>(() => dim.AddEdge("a", "missing", 1f));
        }

        [Test]
        public void Dimension_AddField_FieldIsRetrievable()
        {
            var dim = new Dimension();
            var field = dim.AddField("f1", new Odengine.Fields.FieldProfile("f1"));
            Assert.IsNotNull(field);
            Assert.AreSame(field, dim.GetField("f1"));
        }

        [Test]
        public void Dimension_AddField_DuplicateIdThrows()
        {
            var dim = new Dimension();
            dim.AddField("f1", new Odengine.Fields.FieldProfile("f1"));
            Assert.Throws<InvalidOperationException>(() =>
                dim.AddField("f1", new Odengine.Fields.FieldProfile("f1")));
        }

        [Test]
        public void Dimension_GetField_ReturnsNullForMissing()
        {
            var dim = new Dimension();
            Assert.IsNull(dim.GetField("nonexistent"));
        }

        [Test]
        public void Dimension_GetOrCreateField_CreatesIfMissing()
        {
            var dim = new Dimension();
            var profile = new Odengine.Fields.FieldProfile("f1");
            var f1 = dim.GetOrCreateField("f1", profile);
            var f2 = dim.GetOrCreateField("f1", profile);
            Assert.AreSame(f1, f2);
        }
    }
}
