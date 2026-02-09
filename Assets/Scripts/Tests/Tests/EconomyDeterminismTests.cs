using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Fields;
using System.Collections.Generic;

namespace Odengine.Tests
{
    /// <summary>
    /// Determinism tests: same inputs = same outputs, always
    /// </summary>
    [TestFixture]
    public class EconomyDeterminismTests
    {
        [Test]
        public void SameInitialState_SameTicks_IdenticalResults()
        {
            // Create two identical dimensions
            var dim1 = CreateTestDimension();
            var dim2 = CreateTestDimension();

            var eco1 = new EconomyEngine(dim1);
            var eco2 = new EconomyEngine(dim2);

            var water = new ItemDef("water", 10f);
            eco1.RegisterItem(water);
            eco2.RegisterItem(water);

            eco1.SetAvailability("water", "market1", 100f);
            eco2.SetAvailability("water", "market1", 100f);

            // Tick both 50 times
            for (int i = 0; i < 50; i++)
            {
                dim1.Step(1f);
                dim2.Step(1f);
            }

            // Sample prices at all nodes
            var nodes = new[] { "market1", "market2", "market3" };
            foreach (var node in nodes)
            {
                float price1 = eco1.GetPrice("water", node);
                float price2 = eco2.GetPrice("water", node);
                Assert.AreEqual(price1, price2, 0.0001f, 
                    $"Prices diverged at {node}: {price1} vs {price2}");
            }
        }

        [Test]
        public void IdenticalTradeSequence_ProducesSameOutcome()
        {
            var dim1 = CreateTestDimension();
            var dim2 = CreateTestDimension();

            var eco1 = new EconomyEngine(dim1);
            var eco2 = new EconomyEngine(dim2);

            var water = new ItemDef("water", 10f);
            eco1.RegisterItem(water);
            eco2.RegisterItem(water);

            eco1.SetAvailability("water", "market1", 100f);
            eco2.SetAvailability("water", "market1", 100f);

            // Apply identical trade sequence
            var trades = new[]
            {
                ("market1", -10f), // buy 10
                ("market1", -5f),  // buy 5
                ("market2", -20f), // buy 20
                ("market1", +8f)   // sell 8
            };

            foreach (var (node, delta) in trades)
            {
                eco1.ModifyAvailability("water", node, delta);
                eco2.ModifyAvailability("water", node, delta);
                dim1.Step(0.1f);
                dim2.Step(0.1f);
            }

            // Final state must match
            foreach (var node in new[] { "market1", "market2", "market3" })
            {
                float av1 = eco1.GetAvailability("water", node);
                float av2 = eco2.GetAvailability("water", node);
                Assert.AreEqual(av1, av2, 0.0001f, $"Availability mismatch at {node}");
            }
        }

        [Test]
        public void DifferentNodeIterationOrder_StillDeterministic()
        {
            // This tests that we're using sorted iteration internally
            var dim = CreateTestDimension();
            var eco = new EconomyEngine(dim);

            var water = new ItemDef("water", 10f);
            eco.RegisterItem(water);

            // Set availability in different orders
            eco.SetAvailability("water", "market3", 50f);
            eco.SetAvailability("water", "market1", 100f);
            eco.SetAvailability("water", "market2", 75f);

            dim.Step(1f);

            // Second run
            var dim2 = CreateTestDimension();
            var eco2 = new EconomyEngine(dim2);
            eco2.RegisterItem(water);

            eco2.SetAvailability("water", "market1", 100f);
            eco2.SetAvailability("water", "market2", 75f);
            eco2.SetAvailability("water", "market3", 50f);

            dim2.Step(1f);

            // Results must match despite different initialization order
            foreach (var node in new[] { "market1", "market2", "market3" })
            {
                float av1 = eco.GetAvailability("water", node);
                float av2 = eco2.GetAvailability("water", node);
                Assert.AreEqual(av1, av2, 0.0001f, $"Order-dependent result at {node}");
            }
        }

        private Dimension CreateTestDimension()
        {
            var dim = new Dimension();
            dim.AddNode("market1");
            dim.AddNode("market2");
            dim.AddNode("market3");
            dim.AddEdge("market1", "market2", 1f, EdgeTags.None);
            dim.AddEdge("market2", "market3", 1f, EdgeTags.None);
            dim.AddEdge("market3", "market1", 2f, EdgeTags.None);
            return dim;
        }
    }
}
