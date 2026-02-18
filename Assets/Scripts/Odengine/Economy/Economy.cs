using System;
using System.Collections.Generic;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Economy
{
    /// <summary>
    /// Economy field set: availability (multiplier) and pricePressure (multiplier).
    /// Price = BaseValue * PricePressure / max(Availability, 0.0001)
    /// </summary>
    public sealed class Economy
    {
        private readonly Dimension _dimension;
        private readonly Dictionary<string, ItemDef> _items = new Dictionary<string, ItemDef>(StringComparer.Ordinal);

        public ScalarField Availability { get; }
        public ScalarField PricePressure { get; }

        // Trade injection constants
        public float BuyAvailabilityImpact { get; set; } = -0.1f;
        public float BuyPressureImpact { get; set; } = 0.05f;
        public float SellAvailabilityImpact { get; set; } = 0.1f;
        public float SellPressureImpact { get; set; } = -0.05f;

        public Economy(Dimension dimension)
        {
            _dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));

            var profile = new FieldProfile("economy")
            {
                PropagationRate = 0.3f,
                EdgeResistanceScale = 0.5f,
                DecayRate = 0.01f,
                MinLogAmpClamp = -10f,
                MaxLogAmpClamp = 10f
            };

            Availability = dimension.GetOrCreateField("economy.availability", profile);
            PricePressure = dimension.GetOrCreateField("economy.pricePressure", profile);
        }

        public void RegisterItem(ItemDef item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _items[item.Id] = item;
        }

        public float SamplePrice(string itemId, string nodeId)
        {
            if (!_items.TryGetValue(itemId, out var item))
                throw new InvalidOperationException($"Item '{itemId}' not registered");

            float availMult = Availability.GetMultiplier(nodeId, itemId);
            float pressureMult = PricePressure.GetMultiplier(nodeId, itemId);

            // Neutral baseline: avail=1, pressure=1 → price = baseValue
            float price = item.BaseValue * pressureMult / Math.Max(availMult, 0.0001f);
            return price;
        }

        public void ProcessTrade(string itemId, string nodeId, float units)
        {
            // Positive units = buy (decrease availability, increase pressure)
            // Negative units = sell (increase availability, decrease pressure)

            if (units > 0f) // Buy
            {
                Availability.AddLogAmp(nodeId, itemId, BuyAvailabilityImpact * units);
                PricePressure.AddLogAmp(nodeId, itemId, BuyPressureImpact * units);
            }
            else if (units < 0f) // Sell
            {
                float sellUnits = -units;
                Availability.AddLogAmp(nodeId, itemId, SellAvailabilityImpact * sellUnits);
                PricePressure.AddLogAmp(nodeId, itemId, SellPressureImpact * sellUnits);
            }
        }

        public void Tick(float deltaTime)
        {
            Propagator.Step(_dimension, Availability, deltaTime);
            Propagator.Step(_dimension, PricePressure, deltaTime);
        }
    }
}
