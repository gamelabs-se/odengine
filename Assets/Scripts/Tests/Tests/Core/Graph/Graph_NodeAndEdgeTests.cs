using System;
using NUnit.Framework;
using Odengine.Graph;

namespace Odengine.Tests.Core.Graph
{
    [TestFixture]
    public class Graph_NodeTests
    {
        [Test]
        public void Node_IdIsStoredExactly()
        {
            var node = new Node("alpha");
            Assert.AreEqual("alpha", node.Id);
        }

        [Test]
        public void Node_NameDefaultsToId_WhenNotProvided()
        {
            var node = new Node("beta");
            Assert.AreEqual("beta", node.Name);
        }

        [Test]
        public void Node_CustomNameIsStored()
        {
            var node = new Node("n1", "My Node");
            Assert.AreEqual("My Node", node.Name);
        }

        [Test]
        public void Node_IdAndNameAreIndependent()
        {
            var node = new Node("id-001", "Display Name");
            Assert.AreEqual("id-001", node.Id);
            Assert.AreEqual("Display Name", node.Name);
        }

        [Test]
        public void Node_NameIsSettable()
        {
            var node = new Node("n2", "Old");
            node.Name = "New";
            Assert.AreEqual("New", node.Name);
        }

        [Test]
        public void Node_IdCannotBeEmpty_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Node(""));
        }

        [Test]
        public void Node_IdCannotBeNull_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Node(null));
        }

        [Test]
        public void Node_ToStringContainsId()
        {
            var node = new Node("station-7");
            StringAssert.Contains("station-7", node.ToString());
        }

        [Test]
        public void Node_IdsAreCaseSensitive()
        {
            var lower = new Node("alpha");
            var upper = new Node("Alpha");
            Assert.AreNotEqual(lower.Id, upper.Id);
        }
    }

    [TestFixture]
    public class Graph_EdgeTests
    {
        [Test]
        public void Edge_PropertiesStoredCorrectly()
        {
            var edge = new Edge("a", "b", 2.5f);
            Assert.AreEqual("a", edge.FromId);
            Assert.AreEqual("b", edge.ToId);
            Assert.AreEqual(2.5f, edge.Resistance, 0.00001f);
        }

        [Test]
        public void Edge_ZeroResistanceIsValid()
        {
            Assert.DoesNotThrow(() => new Edge("a", "b", 0f));
        }

        [Test]
        public void Edge_NegativeResistanceThrows()
        {
            Assert.Throws<ArgumentException>(() => new Edge("a", "b", -0.001f));
        }

        [Test]
        public void Edge_EmptyFromIdThrows()
        {
            Assert.Throws<ArgumentException>(() => new Edge("", "b", 1f));
        }

        [Test]
        public void Edge_NullFromIdThrows()
        {
            Assert.Throws<ArgumentException>(() => new Edge(null, "b", 1f));
        }

        [Test]
        public void Edge_EmptyToIdThrows()
        {
            Assert.Throws<ArgumentException>(() => new Edge("a", "", 1f));
        }

        [Test]
        public void Edge_NullToIdThrows()
        {
            Assert.Throws<ArgumentException>(() => new Edge("a", null, 1f));
        }

        [Test]
        public void Edge_NoTagsGivesEmptySet()
        {
            var edge = new Edge("a", "b", 1f);
            Assert.IsNotNull(edge.Tags);
            Assert.AreEqual(0, edge.Tags.Count);
        }

        [Test]
        public void Edge_TagsStoredCorrectly()
        {
            var edge = new Edge("a", "b", 1f, "sea", "trade");
            Assert.IsTrue(edge.Tags.Contains("sea"));
            Assert.IsTrue(edge.Tags.Contains("trade"));
        }

        [Test]
        public void Edge_HasTag_ReturnsTrueForKnownTag()
        {
            var edge = new Edge("a", "b", 1f, "land");
            Assert.IsTrue(edge.HasTag("land"));
        }

        [Test]
        public void Edge_HasTag_ReturnsFalseForUnknownTag()
        {
            var edge = new Edge("a", "b", 1f, "land");
            Assert.IsFalse(edge.HasTag("sea"));
        }

        [Test]
        public void Edge_HasTag_CaseSensitive()
        {
            var edge = new Edge("a", "b", 1f, "Land");
            Assert.IsFalse(edge.HasTag("land"), "Tags are ordinal/case-sensitive");
            Assert.IsTrue(edge.HasTag("Land"));
        }

        [Test]
        public void Edge_DuplicateTagsStoredOnce()
        {
            var edge = new Edge("a", "b", 1f, "sea", "sea", "sea");
            Assert.AreEqual(1, edge.Tags.Count);
        }

        [Test]
        public void Edge_MultipleTagsAllQueryable()
        {
            var edge = new Edge("a", "b", 1f, "land", "road", "paved");
            Assert.IsTrue(edge.HasTag("land"));
            Assert.IsTrue(edge.HasTag("road"));
            Assert.IsTrue(edge.HasTag("paved"));
            Assert.IsFalse(edge.HasTag("sea"));
        }

        [Test]
        public void Edge_NullTagsArray_GivesEmptySet()
        {
            var edge = new Edge("a", "b", 1f, (string[])null);
            Assert.AreEqual(0, edge.Tags.Count);
        }

        [Test]
        public void Edge_ToStringContainsFromAndTo()
        {
            var edge = new Edge("source", "dest", 3.0f);
            string str = edge.ToString();
            StringAssert.Contains("source", str);
            StringAssert.Contains("dest", str);
        }
    }
}
