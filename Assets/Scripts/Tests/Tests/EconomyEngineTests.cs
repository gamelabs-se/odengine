using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Fields;

namespace Odengine.Tests
{
    [TestFixture]
    public class EconomyEngineTests
    {
        [Test]
        public void EconomyEngine_InitializesFieldsCorrectly()
        {
            var dim = new Dimension();
            var economy = new EconomyEngine(dim);

            Assert.IsNotNull(economy.Availability);
            Assert.IsNotNull(economy.Price);
            Assert.AreEqual("economy.availability", economy.Availability.FieldId);
            Assert.AreEqual("economy.price", economy.Price.FieldId);
        }

        [Test]
        public void RegisterItem_CreatesVirtualChannels()
        {
            var dim = new Dimension();
            var economy = new EconomyEngine(dim);

            var item = new ItemDef("water", 10f);
            economy.RegisterItem(item);

            var water = economy.Availability.For("water");
            Assert.IsNotNull(water);
            Assert.AreEqual(0f, water.GetAmp("node1")); // Default amp
        }

        [Test]
        public void SetAvailability_CreatesChannelIfNeeded()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            var economy = new EconomyEngine(dim);

            var item = new ItemDef("water", 10f);
            economy.RegisterItem(item);

            economy.SetAvailability("water", "market", 50f);

            var water = economy.Availability.For("water");
            Assert.AreEqual(50f, water.GetAmp("market"));
        }

        [Test]
        public void SamplePrice_UsesBaseValueWhenNoChannel()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            var economy = new EconomyEngine(dim);

            var item = new ItemDef("water", 100f);
            economy.RegisterItem(item);

            float price = economy.SamplePrice("water", "market");

            // No channel = use field base amp (default 1.0) * baseValue
            Assert.AreEqual(100f, price, 0.01f);
        }

        [Test]
        public void SamplePrice_UsesChannelWhenAvailable()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            var economy = new EconomyEngine(dim);

            var item = new ItemDef("water", 100f);
            economy.RegisterItem(item);

            // Set very low availability → price should go up
            economy.SetAvailability("water", "market", 0.1f);

            float price = economy.SamplePrice("water", "market");

            Assert.Greater(price, 100f); // Price increases with scarcity
        }

        [Test]
        public void ProcessTrade_Buy_DecreasesAvailability()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            var economy = new EconomyEngine(dim);

            var item = new ItemDef("water", 10f);
            economy.RegisterItem(item);
            economy.SetAvailability("water", "market", 100f);

            float before = economy.Availability.For("water").GetAmp("market");

            economy.ProcessTrade("water", "market", 10, true); // Buy 10 units

            float after = economy.Availability.For("water").GetAmp("market");

            Assert.Less(after, before); // Availability decreases after buying
        }

        [Test]
        public void ProcessTrade_Sell_IncreasesAvailability()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            var economy = new EconomyEngine(dim);

            var item = new ItemDef("water", 10f);
            economy.RegisterItem(item);
            economy.SetAvailability("water", "market", 100f);

            float before = economy.Availability.For("water").GetAmp("market");

            economy.ProcessTrade("water", "market", 10, false); // Sell 10 units

            float after = economy.Availability.For("water").GetAmp("market");

            Assert.Greater(after, before); // Availability increases after selling
        }

        [Test]
        public void Tick_PropagatesAvailability()
        {
            var dim = new Dimension();
            var market = dim.AddNode("market");
            var remote = dim.AddNode("remote");
            dim.AddEdge("market", "remote", 1.0f);

            var economy = new EconomyEngine(dim);
            var item = new ItemDef("water", 10f);
            economy.RegisterItem(item);

            economy.SetAvailability("water", "market", 100f);

            float beforeRemote = economy.Availability.For("water").GetAmp("remote");

            economy.Tick(1.0f);

            float afterRemote = economy.Availability.For("water").GetAmp("remote");

            Assert.Greater(afterRemote, beforeRemote); // Availability propagated
        }
    }
}
