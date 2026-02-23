using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Economy
{
    public sealed class EconomySystem
    {
        private readonly Dimension _dimension;
        public readonly ScalarField Availability;
        public readonly ScalarField PricePressure;

        public EconomySystem(Dimension dimension, FieldProfile profile)
        {
            _dimension = dimension;
            // GetOrCreateField: safe for both fresh start and post-snapshot resume.
            Availability  = dimension.GetOrCreateField("economy.availability",  profile);
            PricePressure = dimension.GetOrCreateField("economy.pricePressure", profile);
        }

        public float SamplePrice(string nodeId, string itemId, float baseValue)
        {
            float availMult = Availability.ForChannel(itemId).GetMultiplier(nodeId);
            float pressureMult = PricePressure.ForChannel(itemId).GetMultiplier(nodeId);
            return baseValue * pressureMult / System.Math.Max(availMult, 0.0001f);
        }

        public void InjectTrade(string nodeId, string itemId, float units, float availK = 0.01f, float pressureM = 0.01f)
        {
            Availability.AddLogAmp(nodeId, itemId, -availK * units);
            PricePressure.AddLogAmp(nodeId, itemId, pressureM * units);
        }
    }
}
