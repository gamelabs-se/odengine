using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Fields;

namespace Odengine.Tests
{
    /// <summary>
    /// Tests for edge tags and field-specific resistance behavior
    /// </summary>
    [TestFixture]
    public class EdgeTagResistanceTests
    {
        [Test]
        public void OceanEdge_HighResistanceForEconomy()
        {
            var dim = new Dimension();
            dim.AddNode("island1");
            dim.AddNode("island2");
            dim.AddNode("port");

            // Ocean edge has high resistance + ocean tag
            dim.AddEdge("island1", "island2", 10f, EdgeTags.Ocean);
            dim.AddEdge("island1", "port", 1f, EdgeTags.None);

            var eco = new EconomyEngine(dim);
            var water = new ItemDef("water", 10f);
            eco.RegisterItem(water);

            // Start with availability at island1
            eco.SetAvailability("water", "island1", 100f);

            // Propagate
            for (int i = 0; i < 10; i++)
            {
                dim.Step(1f);
            }

            // Port should have more than island2 (ocean blocks)
            float atPort = eco.GetAvailability("water", "port");
            float atIsland2 = eco.GetAvailability("water", "island2");

            Assert.Greater(atPort, atIsland2, "Port should receive more than across ocean");
        }

        [Test]
        public void RoadEdge_LowResistanceForEconomy()
        {
            var dim = new Dimension();
            dim.AddNode("city1");
            dim.AddNode("city2");
            dim.AddNode("village");

            dim.AddEdge("city1", "city2", 1f, EdgeTags.Road);
            dim.AddEdge("city1", "village", 1f, EdgeTags.None);

            var eco = new EconomyEngine(dim);
            var food = new ItemDef("food", 15f);
            eco.RegisterItem(food);

            eco.SetAvailability("food", "city1", 100f);

            for (int i = 0; i < 10; i++)
            {
                dim.Step(1f);
            }

            // City2 (road) should have more than village
            float atCity2 = eco.GetAvailability("food", "city2");
            float atVillage = eco.GetAvailability("food", "village");

            Assert.Greater(atCity2, atVillage, "Road should facilitate propagation");
        }

        [Test]
        public void MultipleEdgeTags_CombinedEffect()
        {
            var dim = new Dimension();
            dim.AddNode("source");
            dim.AddNode("dest");

            // Edge with multiple tags
            dim.AddEdge("source", "dest", 5f, EdgeTags.Ocean | EdgeTags.Border);

            var eco = new EconomyEngine(dim);
            var item = new ItemDef("test", 10f);
            eco.RegisterItem(item);

            eco.SetAvailability("test", "source", 100f);

            for (int i = 0; i < 5; i++)
            {
                dim.Step(1f);
            }

            // Should propagate very little (both tags increase resistance)
            float atDest = eco.GetAvailability("test", "dest");
            Assert.Less(atDest, 10f, "Multiple restrictive tags should compound");
        }

        [Test]
        public void SameResistance_DifferentTags_DifferentPropagation()
        {
            // Two paths with same base resistance but different tags
            var dim = new Dimension();
            dim.AddNode("source");
            dim.AddNode("dest1");
            dim.AddNode("dest2");

            dim.AddEdge("source", "dest1", 5f, EdgeTags.Ocean);
            dim.AddEdge("source", "dest2", 5f, EdgeTags.Road);

            var eco = new EconomyEngine(dim);
            var item = new ItemDef("test", 10f);
            eco.RegisterItem(item);

            eco.SetAvailability("test", "source", 100f);

            for (int i = 0; i < 10; i++)
            {
                dim.Step(1f);
            }

            float atDest1 = eco.GetAvailability("test", "dest1");
            float atDest2 = eco.GetAvailability("test", "dest2");

            // Road should get more despite same base resistance
            Assert.Greater(atDest2, atDest1, "Tag modifiers should affect propagation");
        }
    }
}
