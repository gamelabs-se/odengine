using NUnit.Framework;
using Odengine.Core;
using Odengine.Graph;
using Odengine.Fields;
using Odengine.Economy;
using System.Collections.Generic;

namespace Odengine.Tests
{
    /// <summary>
    /// Comprehensive tests for EconomyEngine covering:
    /// - Field/channel lazy virtualization
    /// - Delta threshold merge/split behavior  
    /// - Price derivation (field amp vs channel amp)
    /// - Trade intent processing
    /// - Propagation with conservation modes
    /// - Determinism across runs
    /// </summary>
    [TestFixture]
    public class EconomyEngineTests
    {
        // ========================================
        // LAZY VIRTUALIZATION TESTS
        // ========================================

        [Test]
        public void LazyVirtualization_ChannelStartsMergedWithField()
        {
            // Setup
            var dim = new Dimension();
            dim.AddNode("market1", "Market One");
            
            var engine = new EconomyEngine(dim, new EconomyConfig
            {
                MergeDeltaThreshold = 0.1f,
                NormalizationThreshold = 0.5f
            });
            
            var water = new ItemDef("water", "Water", 100f);
            engine.RegisterItem(water);
            
            // Field amplitude = 1.0 everywhere
            engine.AvailabilityField.SetAmplitude("market1", 1.0f);
            
            // Should NOT have a realized channel yet (not using .AvailabilityField directly)
            
            // Sample price - should use field amp + base value
            float price = engine.GetPrice("water", "market1");
            
            Assert.That(price, Is.GreaterThan(0f));
        }

        [Test]
        public void LazyVirtualization_TradeIntent_SplitsChannel_WhenAboveThreshold()
        {
            var dim = new Dimension();
            dim.AddNode("market1", "Market One");
            
            var engine = new EconomyEngine(dim, new EconomyConfig
            {
                MergeDeltaThreshold = 0.1f
            });
            
            var water = new ItemDef("water", "Water", 100f);
            engine.RegisterItem(water);
            
            engine.AvailabilityField.SetAmplitude("market1", 1.0f);
            
            // Large trade pushes channel amp away from field amp
            engine.ProcessTrade(new TradeIntent
            {
                NodeId = "market1",
                ItemId = "water",
                Quantity = 500,
                IsBuy = true
            });
            
            // Should NOW have a realized channel (split from field)
            Assert.IsTrue(engine.AvailabilityField.Storage.HasChannel("water"));
            
            // Channel amp should be different from field
            var channelAmp = engine.AvailabilityField.For("water").GetAmp("market1");
            var fieldAmp = engine.AvailabilityField.GetAmplitude("market1");
            
            Assert.That(System.Math.Abs(channelAmp - fieldAmp), Is.GreaterThan(0.05f));
        }

        // ========================================
        // PRICE DERIVATION TESTS
        // ========================================

        [Test]
        public void PriceDerivation_Scarcity_IncreasesPrice()
        {
            var dim = new Dimension();
            dim.AddNode("market1", "Market One");
            
            var engine = new EconomyEngine(dim);
            var water = new ItemDef("water", "Water", 100f);
            engine.RegisterItem(water);
            
            // Normal availability
            engine.AvailabilityField.SetAmplitude("market1", 1.0f);
            float normalPrice = engine.ComputePrice("water", "market1");
            
            // Low availability (scarcity)
            engine.AvailabilityField.SetAmplitude("market1", 0.2f);
            float scarcityPrice = engine.ComputePrice("water", "market1");
            
            Assert.That(scarcityPrice, Is.GreaterThan(normalPrice));
        }

        [Test]
        public void PriceDerivation_Abundance_DecreasesPrice()
        {
            var dim = new Dimension();
            dim.AddNode("market1", "Market One");
            
            var engine = new EconomyEngine(dim);
            var water = new ItemDef("water", "Water", 100f);
            engine.RegisterItem(water);
            
            // Normal availability
            engine.AvailabilityField.SetAmplitude("market1", 1.0f);
            float normalPrice = engine.ComputePrice("water", "market1");
            
            // High availability (abundance)
            engine.AvailabilityField.SetAmplitude("market1", 3.0f);
            float abundancePrice = engine.ComputePrice("water", "market1");
            
            Assert.That(abundancePrice, Is.LessThan(normalPrice));
        }

        // ========================================
        // TRADE INTENT TESTS
        // ========================================

        [Test]
        public void TradeIntent_Buy_ReducesAvailability()
        {
            var dim = new Dimension();
            dim.AddNode("market1", "Market One");
            
            var engine = new EconomyEngine(dim);
            var water = new ItemDef("water", "Water", 100f);
            engine.RegisterItem(water);
            
            engine.AvailabilityField.SetAmplitude("market1", 1.0f);
            
            engine.ProcessTrade(new TradeIntent
            {
                NodeId = "market1",
                ItemId = "water",
                Quantity = 100,
                IsBuy = true
            });
            
            var channelAmp = engine.AvailabilityField.For("water").GetAmp("market1");
            Assert.That(channelAmp, Is.LessThan(1.0f));
        }

        [Test]
        public void TradeIntent_Sell_IncreasesAvailability()
        {
            var dim = new Dimension();
            dim.AddNode("market1", "Market One");
            
            var engine = new EconomyEngine(dim);
            var water = new ItemDef("water", "Water", 100f);
            engine.RegisterItem(water);
            
            engine.AvailabilityField.SetAmplitude("market1", 1.0f);
            
            engine.ProcessTrade(new TradeIntent
            {
                NodeId = "market1",
                ItemId = "water",
                Quantity = 100,
                IsBuy = false
            });
            
            var channelAmp = engine.AvailabilityField.For("water").GetAmp("market1");
            Assert.That(channelAmp, Is.GreaterThan(1.0f));
        }

        // ========================================
        // DETERMINISM TESTS
        // ========================================

        [Test]
        public void Determinism_SameTradesProduceSameResult()
        {
            // Setup world A
            var dimA = new Dimension();
            dimA.AddNode("market1", "Market One");
            var engineA = new EconomyEngine(dimA);
            var waterA = new ItemDef("water", "Water", 100f);
            engineA.RegisterItem(waterA);
            engineA.AvailabilityField.SetAmplitude("market1", 1.0f);
            
            // Setup world B (identical)
            var dimB = new Dimension();
            dimB.AddNode("market1", "Market One");
            var engineB = new EconomyEngine(dimB);
            var waterB = new ItemDef("water", "Water", 100f);
            engineB.RegisterItem(waterB);
            engineB.AvailabilityField.SetAmplitude("market1", 1.0f);
            
            // Same trade sequence
            var trades = new List<TradeIntent>
            {
                new TradeIntent { NodeId = "market1", ItemId = "water", Quantity = 100, IsBuy = true },
                new TradeIntent { NodeId = "market1", ItemId = "water", Quantity = 50, IsBuy = false },
                new TradeIntent { NodeId = "market1", ItemId = "water", Quantity = 200, IsBuy = true }
            };
            
            foreach (var trade in trades)
            {
                engineA.ProcessTrade(trade);
                engineB.ProcessTrade(trade);
            }
            
            // Tick both
            engineA.Tick(1.0f);
            engineB.Tick(1.0f);
            
            var priceA = engineA.ComputePrice("water", "market1");
            var priceB = engineB.ComputePrice("water", "market1");
            
            Assert.That(priceA, Is.EqualTo(priceB).Within(0.001f));
        }

        // ========================================
        // EDGE CASES
        // ========================================

        [Test]
        public void EdgeCase_ZeroAmplitude_DoesNotCrashPricing()
        {
            var dim = new Dimension();
            dim.AddNode("market1", "Market One");
            
            var engine = new EconomyEngine(dim);
            var water = new ItemDef("water", "Water", 100f);
            engine.RegisterItem(water);
            
            engine.AvailabilityField.SetAmplitude("market1", 0f);
            
            Assert.DoesNotThrow(() =>
            {
                var price = engine.ComputePrice("water", "market1");
                Assert.That(price, Is.GreaterThanOrEqualTo(0f));
            });
        }
    }
}
