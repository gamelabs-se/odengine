using Odengine.Fields;

namespace Odengine.Economy
{
    /// <summary>
    /// Economy uses TWO channeled fields:
    /// - Availability (how much of an item is present)
    /// - Price (price pressure/demand)
    /// 
    /// That's it. No "supply" or "demand" fields.
    /// Demand is emergent from many actors pulling from availability.
    /// </summary>
    public sealed class EconomyFields
    {
        public ChanneledField Availability { get; }
        public ChanneledField Price { get; }

        public EconomyFields(FieldProfile availabilityProfile, FieldProfile priceProfile)
        {
            Availability = new ChanneledField("economy.availability", availabilityProfile);
            Price = new ChanneledField("economy.price", priceProfile);
        }

        /// <summary>
        /// Sample price for an item at a location.
        /// Uses Availability amp and Price amp to compute final price.
        /// </summary>
        public float SamplePrice(string nodeId, string itemId, float baseValue)
        {
            float availabilityAmp = Availability.GetAmplitude(nodeId, itemId);
            float priceAmp = Price.GetAmplitude(nodeId, itemId);
            
            // Simple formula: base * (1 + priceAmp) / (1 + availabilityAmp)
            // High availability → lower price
            // High price pressure → higher price
            float scarcityFactor = 1f / (1f + availabilityAmp);
            float demandFactor = 1f + priceAmp;
            
            return baseValue * scarcityFactor * demandFactor;
        }
    }
}
