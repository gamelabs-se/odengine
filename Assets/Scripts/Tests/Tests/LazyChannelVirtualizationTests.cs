using NUnit.Framework;
using Odengine.Core;
using Odengine.Economy;
using Odengine.Fields;

namespace Odengine.Tests
{
    /// <summary>
    /// Tests for lazy channel virtualization: merge/split based on delta thresholds
    /// </summary>
    [TestFixture]
    public class LazyChannelVirtualizationTests
    {
        [Test]
        public void ChannelWithinDelta_MergesIntoFieldAmp()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var item = new ItemDef("water", 10f);
            eco.RegisterItem(item);

            // Set field amp to 100
            eco.AvailabilityField.SetBaseAmplitude("market", 100f);

            // Set channel slightly different (within delta of 10)
            eco.SetAvailability("water", "market", 105f);

            // Tick to trigger merge check
            dim.Step(1f);

            // Channel should merge - verify it's no longer explicitly tracked
            bool hasChannel = eco.AvailabilityField.Storage.HasActiveChannel("water", "market");
            Assert.IsFalse(hasChannel, "Channel should have merged into field amp");

            // But we can still read it (falls back to field amp)
            float amp = eco.GetAvailability("water", "market");
            Assert.AreEqual(100f, amp, 1f); // Within field range
        }

        [Test]
        public void ChannelBeyondDelta_StaysTrackedIndividually()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var item = new ItemDef("water", 10f);
            eco.RegisterItem(item);

            eco.AvailabilityField.SetBaseAmplitude("market", 100f);

            // Set channel significantly different (beyond delta threshold)
            eco.SetAvailability("water", "market", 150f);

            dim.Step(1f);

            // Channel should remain tracked
            bool hasChannel = eco.AvailabilityField.Storage.HasActiveChannel("water", "market");
            Assert.IsTrue(hasChannel, "Channel should remain individually tracked");

            float amp = eco.GetAvailability("water", "market");
            Assert.AreEqual(150f, amp, 0.1f);
        }

        [Test]
        public void TradeIntent_PushesChannelBeyondThreshold_SplitsFromField()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var item = new ItemDef("water", 10f);
            eco.RegisterItem(item);

            // Start with uniform field
            eco.AvailabilityField.SetBaseAmplitude("market", 100f);

            // Trade pushes it beyond delta
            eco.ModifyAvailability("water", "market", -50f); // Now 50

            dim.Step(0.1f);

            // Should now be tracked individually
            bool hasChannel = eco.AvailabilityField.Storage.HasActiveChannel("water", "market");
            Assert.IsTrue(hasChannel, "Trade should have split channel from field");

            float amp = eco.GetAvailability("water", "market");
            Assert.AreEqual(50f, amp, 1f);
        }

        [Test]
        public void MultipleItems_OnlySplitChannelsTracked()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var water = new ItemDef("water", 10f);
            var food = new ItemDef("food", 15f);
            var medicine = new ItemDef("medicine", 50f);

            eco.RegisterItem(water);
            eco.RegisterItem(food);
            eco.RegisterItem(medicine);

            // Set uniform field amp
            eco.AvailabilityField.SetBaseAmplitude("market", 100f);

            // Only push water beyond threshold
            eco.ModifyAvailability("water", "market", -60f);

            dim.Step(0.1f);

            // Only water should be tracked
            Assert.IsTrue(eco.AvailabilityField.Storage.HasActiveChannel("water", "market"));
            Assert.IsFalse(eco.AvailabilityField.Storage.HasActiveChannel("food", "market"));
            Assert.IsFalse(eco.AvailabilityField.Storage.HasActiveChannel("medicine", "market"));
        }

        [Test]
        public void ChannelNormalization_PullsTowardFieldAmp()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var item = new ItemDef("water", 10f);
            eco.RegisterItem(item);

            eco.AvailabilityField.SetBaseAmplitude("market", 100f);
            eco.SetAvailability("water", "market", 120f);

            float before = eco.GetAvailability("water", "market");

            // Tick multiple times - should normalize toward field
            for (int i = 0; i < 10; i++)
            {
                dim.Step(1f);
            }

            float after = eco.GetAvailability("water", "market");

            // Should have moved closer to 100
            Assert.Less(after, before, "Channel should normalize toward field amp");
            Assert.Greater(after, 100f, "But not overshoot");
        }

        [Test]
        public void SpikedChannel_OnlyNormalizesIfBelowThreshold()
        {
            var dim = new Dimension();
            dim.AddNode("market");
            
            var eco = new EconomyEngine(dim);
            var item = new ItemDef("water", 10f);
            eco.RegisterItem(item);

            eco.AvailabilityField.SetBaseAmplitude("market", 100f);

            // Massive spike (war/disaster/player action)
            eco.SetAvailability("water", "market", 500f);

            float before = eco.GetAvailability("water", "market");

            // Tick once
            dim.Step(1f);

            float after = eco.GetAvailability("water", "market");

            // Should NOT normalize (spike protection threshold)
            Assert.AreEqual(before, after, 10f, "Spike should be preserved");
        }
    }
}
