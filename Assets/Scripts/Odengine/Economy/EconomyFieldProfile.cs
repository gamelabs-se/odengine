using Odengine.Fields;

namespace Odengine.Economy
{
    public sealed class EconomyFieldProfile : FieldProfile
    {
        public EconomyFieldProfile()
            : base(
                profileId: "economy",
                propagationRate: 0.3f,
                edgeResistanceScale: 0.5f,
                decayRate: 0.05f,
                minAmp: 0.001f,
                mode: ConservationMode.Diffusion
            )
        {
        }
    }
}
