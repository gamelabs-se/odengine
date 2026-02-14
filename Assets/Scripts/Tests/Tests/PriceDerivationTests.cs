using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Fields;
using Odengine.Graph;

namespace Odengine.Tests
{
    /// <summary>
    /// Tests for price derivation from field/channel amplitudes
    /// </summary>
    [TestFixture]
    public class PriceDerivationTests
    {
        [Test]
        public void UnknownChannel_UseFieldAmpAndBaseValue()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var water = new ItemDef("water", 10f);
            eco.RegisterItem(water);

            // Set only field amp (channel not tracked)
            eco.AvailabilityField.SetBaseAmplitude("market", 100f);

            float price = eco.GetPrice("water", "market");

            // Should derive from field amp * base value
            Assert.Greater(price, 0f, "Price should be positive");
            Assert.AreEqual(10f, price, 2f, "Price should be near base value");
        }

        [Test]
        public void TrackedChannel_UsesChannelAmp()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var water = new ItemDef("water", 10f);
            eco.RegisterItem(water);

            eco.AvailabilityField.SetBaseAmplitude("market", 100f);

            // Create scarcity
            eco.SetAvailability("water", "market", 20f);

            float price = eco.GetPrice("water", "market");

            // Scarcity should increase price
            Assert.Greater(price, 10f, "Scarcity should increase price");
        }

        [Test]
        public void HighAvailability_LowerPrice()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var food = new ItemDef("food", 15f);
            eco.RegisterItem(food);

            eco.SetAvailability("food", "market", 200f);

            float price = eco.GetPrice("food", "market");

            // Abundance should lower price
            Assert.Less(price, 15f, "Abundance should lower price");
        }

        [Test]
        public void ZeroAvailability_MaxPrice()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var medicine = new ItemDef("medicine", 50f);
            eco.RegisterItem(medicine);

            eco.SetAvailability("medicine", "market", 0f);

            float price = eco.GetPrice("medicine", "market");

            // Total scarcity = extreme price
            Assert.Greater(price, 100f, "Zero availability should spike price");
        }

        [Test]
        public void PriceDivergence_AfterLocalTrade()
        {
            var dim = new Dimension();
            dim.AddNode("market1");
            dim.AddNode("market2");
            dim.AddEdge("market1", "market2", 5f, "");
            
            var eco = new EconomyEngine(dim);
            var water = new ItemDef("water", 10f);
            eco.RegisterItem(water);

            // Start equal
            eco.SetAvailability("water", "market1", 100f);
            eco.SetAvailability("water", "market2", 100f);

            float price1Before = eco.GetPrice("water", "market1");
            float price2Before = eco.GetPrice("water", "market2");

            Assert.AreEqual(price1Before, price2Before, 0.5f, "Prices should start equal");

            // Trade at market1 (buy = decrease availability)
            eco.ModifyAvailability("water", "market1", -50f);

            dim.Step(0.1f);

            float price1After = eco.GetPrice("water", "market1");
            float price2After = eco.GetPrice("water", "market2");

            // Market1 should have higher price now
            Assert.Greater(price1After, price2After, "Local trade should create price divergence");
        }

        [Test]
        public void PriceConvergence_OverTime()
        {
            var dim = new Dimension();
            dim.AddNode("market1");
            dim.AddNode("market2");
            dim.AddEdge("market1", "market2", 1f, "");
            
            var eco = new EconomyEngine(dim);
            var water = new ItemDef("water", 10f);
            eco.RegisterItem(water);

            eco.SetAvailability("water", "market1", 50f);
            eco.SetAvailability("water", "market2", 150f);

            float initialDivergence = System.Math.Abs(
                eco.GetPrice("water", "market1") - eco.GetPrice("water", "market2"));

            // Propagate
            for (int i = 0; i < 20; i++)
            {
                dim.Step(1f);
            }

            float finalDivergence = System.Math.Abs(
                eco.GetPrice("water", "market1") - eco.GetPrice("water", "market2"));

            // Prices should converge
            Assert.Less(finalDivergence, initialDivergence, "Prices should converge over time");
        }
    }
}
