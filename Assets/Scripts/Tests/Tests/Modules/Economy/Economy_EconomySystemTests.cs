using System;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Fields;

namespace Odengine.Tests.Modules.Economy
{
    /// <summary>
    /// Tests for <see cref="EconomySystem"/>.
    ///
    /// SamplePrice formula: baseValue × pressureMult / max(availMult, 0.0001)
    /// InjectTrade: availability.AddLogAmp(-availK × units), pricePressure.AddLogAmp(+pressureM × units)
    /// </summary>
    [TestFixture]
    public class Economy_EconomySystemTests
    {
        private static FieldProfile DefaultEcoProfile() =>
            new FieldProfile("economy") { PropagationRate = 1f, DecayRate = 0f };

        private static (Dimension dim, EconomySystem economy) Make(
            string nodeId = "market")
        {
            var dim = new Dimension();
            dim.AddNode(nodeId);
            var economy = new EconomySystem(dim, DefaultEcoProfile());
            return (dim, economy);
        }

        // ─── SamplePrice neutral baseline ────────────────────────────────────

        [Test]
        public void SamplePrice_NeutralState_EqualsBaseValue()
        {
            var (dim, eco) = Make();
            Assert.AreEqual(10f, eco.SamplePrice("market", "water", 10f), 1e-5f);
        }

        [Test]
        public void SamplePrice_NeutralState_WithDifferentBaseValues()
        {
            var (dim, eco) = Make();
            Assert.AreEqual(1f, eco.SamplePrice("market", "water", 1f), 1e-5f);
            Assert.AreEqual(100f, eco.SamplePrice("market", "water", 100f), 1e-5f);
            Assert.AreEqual(0f, eco.SamplePrice("market", "water", 0f), 1e-9f);
        }

        [Test]
        public void SamplePrice_NeutralState_IsAlwaysPositive()
        {
            var (_, eco) = Make();
            Assert.Greater(eco.SamplePrice("market", "water", 1f), 0f);
        }

        // ─── InjectTrade effects ─────────────────────────────────────────────

        [Test]
        public void InjectTrade_DecreasesAvailability()
        {
            var (dim, eco) = Make();
            float before = eco.Availability.GetLogAmp("market", "water");
            eco.InjectTrade("market", "water", 100f);
            Assert.Less(eco.Availability.GetLogAmp("market", "water"), before);
        }

        [Test]
        public void InjectTrade_IncreasesPressure()
        {
            var (dim, eco) = Make();
            float before = eco.PricePressure.GetLogAmp("market", "water");
            eco.InjectTrade("market", "water", 100f);
            Assert.Greater(eco.PricePressure.GetLogAmp("market", "water"), before);
        }

        [Test]
        public void InjectTrade_RaisesPrice()
        {
            var (dim, eco) = Make();
            eco.InjectTrade("market", "water", 100f);
            Assert.Greater(eco.SamplePrice("market", "water", 10f), 10f,
                "Buying should raise price (less availability, more pressure)");
        }

        [Test]
        public void InjectTrade_AvailabilityFormula_ExactLogAmpDelta()
        {
            // AddLogAmp("market", "water", -availK * units) where availK=0.01f, units=50f
            float availK = 0.01f;
            float units = 50f;

            var (_, eco) = Make();
            eco.InjectTrade("market", "water", units, availK: availK);

            float expected = -availK * units;
            Assert.AreEqual(expected, eco.Availability.GetLogAmp("market", "water"), 1e-5f);
        }

        [Test]
        public void InjectTrade_PressureFormula_ExactLogAmpDelta()
        {
            float pressureM = 0.01f;
            float units = 50f;

            var (_, eco) = Make();
            eco.InjectTrade("market", "water", units, pressureM: pressureM);

            float expected = pressureM * units;
            Assert.AreEqual(expected, eco.PricePressure.GetLogAmp("market", "water"), 1e-5f);
        }

        [Test]
        public void InjectTrade_CustomK_CustomM_BothApplied()
        {
            float availK = 0.05f;
            float pressureM = 0.02f;
            float units = 20f;

            var (_, eco) = Make();
            eco.InjectTrade("market", "water", units, availK: availK, pressureM: pressureM);

            Assert.AreEqual(-availK * units, eco.Availability.GetLogAmp("market", "water"), 1e-5f);
            Assert.AreEqual(pressureM * units, eco.PricePressure.GetLogAmp("market", "water"), 1e-5f);
        }

        [Test]
        public void MultipleTrades_Additive()
        {
            var (_, eco) = Make();
            eco.InjectTrade("market", "water", 50f);
            eco.InjectTrade("market", "water", 50f);

            float expected = -0.01f * 100f;
            Assert.AreEqual(expected, eco.Availability.GetLogAmp("market", "water"), 1e-5f);
        }

        // ─── SamplePrice formula precision ───────────────────────────────────

        [Test]
        public void SamplePrice_ExactFormula_AfterKnownInjection()
        {
            // After injecting 100 units: availLogAmp = -0.01*100 = -1.0
            //                            pressLogAmp = +0.01*100 = +1.0
            // availMult = exp(-1) ≈ 0.3679, pressMult = exp(1) ≈ 2.7183
            // price = 10 * 2.7183 / 0.3679 ≈ 73.89
            float units = 100f;
            float base_ = 10f;

            var (_, eco) = Make();
            eco.InjectTrade("market", "water", units);

            float availMult = eco.Availability.GetMultiplier("market", "water");
            float pressMult = eco.PricePressure.GetMultiplier("market", "water");
            float expected = base_ * pressMult / MathF.Max(availMult, 0.0001f);

            Assert.AreEqual(expected, eco.SamplePrice("market", "water", base_), 1e-3f);
        }

        // ─── Monotonicity ─────────────────────────────────────────────────────

        [Test]
        public void SamplePrice_MonotonicallyIncreasing_WithMoreTrades()
        {
            var (_, eco) = Make();
            float prev = eco.SamplePrice("market", "water", 10f);
            for (int i = 0; i < 10; i++)
            {
                eco.InjectTrade("market", "water", 10f);
                float current = eco.SamplePrice("market", "water", 10f);
                Assert.Greater(current, prev, $"Price must rise after trade #{i + 1}");
                prev = current;
            }
        }

        // ─── Multi-item isolation ────────────────────────────────────────────

        [Test]
        public void MultipleItems_TradingOneDoesNotAffectAnother()
        {
            var (_, eco) = Make();
            eco.InjectTrade("market", "water", 100f);

            float waterPrice = eco.SamplePrice("market", "water", 10f);
            float orePrice = eco.SamplePrice("market", "ore", 10f);

            Assert.Greater(waterPrice, 10f, "Water price should rise");
            Assert.AreEqual(10f, orePrice, 1e-5f, "Ore price must be unaffected");
        }

        [Test]
        public void MultipleItems_FiftyChannels_AllIsolated()
        {
            var dim = new Dimension();
            dim.AddNode("m");
            var eco = new EconomySystem(dim, new FieldProfile("economy"));

            // Inject only "item000"
            eco.InjectTrade("m", "item000", 100f);

            for (int i = 1; i < 50; i++)
            {
                float price = eco.SamplePrice("m", $"item{i:000}", 10f);
                Assert.AreEqual(10f, price, 1e-5f,
                    $"item{i:000} must be unaffected by item000 trade");
            }
        }

        // ─── EpsilonGuard — availability collapse floor ───────────────────────

        [Test]
        public void EpsilonGuard_ExtremeAvailabilityCollapse_NotInfinity()
        {
            var (_, eco) = Make();
            // Drive availability deeply negative
            eco.InjectTrade("market", "water", 100_000f);
            float price = eco.SamplePrice("market", "water", 1f);
            Assert.IsFalse(float.IsInfinity(price), "Price must never be Infinity");
        }

        [Test]
        public void EpsilonGuard_ExtremeAvailabilityCollapse_NotNaN()
        {
            var (_, eco) = Make();
            eco.InjectTrade("market", "water", 100_000f);
            float price = eco.SamplePrice("market", "water", 1f);
            Assert.IsFalse(float.IsNaN(price), "Price must never be NaN");
        }

        [Test]
        public void EpsilonGuard_PriceAlwaysPositive_AfterExtremeTrades()
        {
            var (_, eco) = Make();
            eco.InjectTrade("market", "water", 100_000f);
            Assert.Greater(eco.SamplePrice("market", "water", 1f), 0f);
        }

        // ─── Edge cases ───────────────────────────────────────────────────────

        [Test]
        public void BaseValueZero_PriceIsZero()
        {
            var (_, eco) = Make();
            eco.InjectTrade("market", "water", 50f);
            Assert.AreEqual(0f, eco.SamplePrice("market", "water", 0f), 1e-9f);
        }

        [Test]
        public void SamplePrice_UnknownNode_ReturnsBaseValue()
        {
            // Unknown node → neutral logAmps → price = baseValue × 1.0 / max(1.0, 0.0001) = baseValue
            var (_, eco) = Make();
            Assert.AreEqual(10f, eco.SamplePrice("ghost-node", "water", 10f), 1e-5f);
        }

        [Test]
        public void SamplePrice_UnknownItem_ReturnsBaseValue()
        {
            var (_, eco) = Make();
            Assert.AreEqual(10f, eco.SamplePrice("market", "void-item", 10f), 1e-5f);
        }

        // ─── Determinism ─────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameOperations_SamePriceResult()
        {
            float RunAndSample()
            {
                var dim = new Dimension();
                dim.AddNode("m");
                var eco = new EconomySystem(dim, new FieldProfile("economy"));
                eco.InjectTrade("m", "water", 50f);
                eco.InjectTrade("m", "water", 30f);
                eco.InjectTrade("m", "ore", 80f);
                return eco.SamplePrice("m", "water", 10f);
            }

            Assert.AreEqual(RunAndSample(), RunAndSample(), 1e-6f);
        }

        // ─── Fields are owned by EconomySystem ───────────────────────────────

        [Test]
        public void AvailabilityField_RegisteredInDimension()
        {
            var (dim, _) = Make();
            Assert.IsNotNull(dim.GetField("economy.availability"));
        }

        [Test]
        public void PricePressureField_RegisteredInDimension()
        {
            var (dim, _) = Make();
            Assert.IsNotNull(dim.GetField("economy.pricePressure"));
        }

        [Test]
        public void AvailabilityAndPressure_AreSeparateFields()
        {
            var (_, eco) = Make();
            Assert.AreNotSame(eco.Availability, eco.PricePressure);
        }

        // ─── Propagation integration ──────────────────────────────────────────

        [Test]
        public void Propagation_AvailabilitySpreadToNeighbor()
        {
            var dim = new Dimension();
            dim.AddNode("market-a");
            dim.AddNode("market-b");
            dim.AddEdge("market-a", "market-b", 0f);
            var eco = new EconomySystem(dim, new FieldProfile("economy"));

            eco.InjectTrade("market-a", "water", 100f);
            float bBefore = eco.Availability.GetLogAmp("market-b", "water");

            Propagator.Step(dim, eco.Availability, 1f);

            Assert.Less(eco.Availability.GetLogAmp("market-b", "water"), bBefore,
                "Availability scarcity must propagate to connected market");
        }

        [Test]
        public void Propagation_PricePressureSpreadToNeighbor()
        {
            var dim = new Dimension();
            dim.AddNode("a"); dim.AddNode("b");
            dim.AddEdge("a", "b", 0f);
            var eco = new EconomySystem(dim, new FieldProfile("economy"));

            eco.InjectTrade("a", "water", 100f);
            Propagator.Step(dim, eco.PricePressure, 1f);

            Assert.Greater(eco.PricePressure.GetLogAmp("b", "water"), 0f,
                "Price pressure must propagate to connected node");
        }

        [Test]
        public void HighResistanceEdge_LimitsEconomicRipple()
        {
            var dim = new Dimension();
            dim.AddNode("a"); dim.AddNode("b");
            dim.AddEdge("a", "b", 20f); // very high resistance
            var eco = new EconomySystem(dim, new FieldProfile("economy"));

            eco.InjectTrade("a", "water", 1000f);
            Propagator.Step(dim, eco.Availability, 1f);

            // Nearly nothing should reach b
            float received = eco.Availability.GetLogAmp("b", "water");
            Assert.AreEqual(0f, received, 0.01f,
                "High resistance must block economic ripple to remote node");
        }
    }
}
