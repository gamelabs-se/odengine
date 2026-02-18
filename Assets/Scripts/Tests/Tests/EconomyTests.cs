using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;

namespace Odengine.Tests
{
    [TestFixture]
    public class EconomyTests
    {
        [Test]
        public void Economy_SamplePrice_NeutralEqualsBaseValue()
        {
            var dimension = new Dimension();
            dimension.AddNode("city");

            var economy = new Economy(dimension);
            var water = new ItemDef("water", "Water", 100f);
            economy.RegisterItem(water);

            // No data stored → availability=1, pressure=1 → price=baseValue
            float price = economy.SamplePrice("water", "city");
            Assert.AreEqual(100f, price, 0.01f);
        }

        [Test]
        public void Economy_TradeInjection_ChangesLocalPrice()
        {
            var dimension = new Dimension();
            dimension.AddNode("city");

            var economy = new Economy(dimension);
            var water = new ItemDef("water", "Water", 100f);
            economy.RegisterItem(water);

            float priceInitial = economy.SamplePrice("water", "city");
            Assert.AreEqual(100f, priceInitial, 0.01f);

            // Buy 10 units (decreases availability, increases pressure)
            economy.ProcessTrade("water", "city", 10f);

            float priceAfter = economy.SamplePrice("water", "city");
            Assert.Greater(priceAfter, priceInitial, "Price should increase after buying");
        }
    }
}
