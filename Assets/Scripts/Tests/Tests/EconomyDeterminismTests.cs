using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;

namespace Odengine.Tests
{
    [TestFixture]
    public class EconomyDeterminismTests
    {
        [Test]
        public void Economy_SameSeed_SameResults()
        {
            // World A
            var dimA = new Dimension();
            dimA.AddNode("n1");
            dimA.AddNode("n2");
            dimA.AddEdge("n1", "n2", 1.0f);

            var econA = new EconomyEngine(dimA);
            var item = new ItemDef("water", 10f);
            econA.RegisterItem(item);
            econA.SetAvailability("water", "n1", 100f);

            econA.Tick(1.0f);
            float priceA = econA.SamplePrice("water", "n1");

            // World B (identical setup)
            var dimB = new Dimension();
            dimB.AddNode("n1");
            dimB.AddNode("n2");
            dimB.AddEdge("n1", "n2", 1.0f);

            var econB = new EconomyEngine(dimB);
            econB.RegisterItem(item);
            econB.SetAvailability("water", "n1", 100f);

            econB.Tick(1.0f);
            float priceB = econB.SamplePrice("water", "n1");

            Assert.AreEqual(priceA, priceB, 0.0001f);
        }

        [Test]
        public void Economy_MultipleTicks_Deterministic()
        {
            // World A
            var dimA = new Dimension();
            dimA.AddNode("n1");
            dimA.AddNode("n2");
            dimA.AddEdge("n1", "n2", 1.0f);

            var econA = new EconomyEngine(dimA);
            var item = new ItemDef("water", 10f);
            econA.RegisterItem(item);
            econA.SetAvailability("water", "n1", 100f);

            for (int i = 0; i < 10; i++)
                econA.Tick(0.1f);

            float priceA = econA.SamplePrice("water", "n2");

            // World B (identical)
            var dimB = new Dimension();
            dimB.AddNode("n1");
            dimB.AddNode("n2");
            dimB.AddEdge("n1", "n2", 1.0f);

            var econB = new EconomyEngine(dimB);
            econB.RegisterItem(item);
            econB.SetAvailability("water", "n1", 100f);

            for (int i = 0; i < 10; i++)
                econB.Tick(0.1f);

            float priceB = econB.SamplePrice("water", "n2");

            Assert.AreEqual(priceA, priceB, 0.0001f);
        }

        [Test]
        public void TradeIntent_Deterministic()
        {
            var dimA = new Dimension();
            dimA.AddNode("market");
            var econA = new EconomyEngine(dimA);
            var item = new ItemDef("water", 10f);
            econA.RegisterItem(item);
            econA.SetAvailability("water", "market", 100f);

            econA.ProcessTrade("water", "market", 10, true);
            econA.Tick(1.0f);
            float priceA = econA.SamplePrice("water", "market");

            var dimB = new Dimension();
            dimB.AddNode("market");
            var econB = new EconomyEngine(dimB);
            econB.RegisterItem(item);
            econB.SetAvailability("water", "market", 100f);

            econB.ProcessTrade("water", "market", 10, true);
            econB.Tick(1.0f);
            float priceB = econB.SamplePrice("water", "market");

            Assert.AreEqual(priceA, priceB, 0.0001f);
        }
    }
}
