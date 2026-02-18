using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Fields;

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

            var profile = new FieldProfile("economy");
            var economy = new EconomySystem(dimension, profile);

            // No data stored → availability=1, pressure=1 → price=baseValue
            float price = economy.SamplePrice("city", "water", 100f);
            Assert.AreEqual(100f, price, 0.01f);
        }

        [Test]
        public void Economy_TradeInjection_ChangesLocalPrice()
        {
            var dimension = new Dimension();
            dimension.AddNode("city");

            var profile = new FieldProfile("economy");
            var economy = new EconomySystem(dimension, profile);

            float priceInitial = economy.SamplePrice("city", "water", 100f);
            Assert.AreEqual(100f, priceInitial, 0.01f);

            // Buy 10 units (decreases availability, increases pressure)
            economy.InjectTrade("city", "water", 10f);

            float priceAfter = economy.SamplePrice("city", "water", 100f);
            Assert.Greater(priceAfter, priceInitial, "Price should increase after buying");
        }
    }
}
