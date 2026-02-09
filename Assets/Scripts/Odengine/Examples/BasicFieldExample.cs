using UnityEngine;
using Odengine.Core;
using Odengine.Fields;
using Odengine.Economy;

namespace Odengine.Examples
{
    /// <summary>
    /// Demonstrates:
    /// - Two-phase propagation (deterministic)
    /// - Conservation modes (Diffusion vs Radiation)
    /// - Channeled fields (economy with items)
    /// - Edge tags + resistance multipliers
    /// </summary>
    public class BasicFieldExample : MonoBehaviour
    {
        void Start()
        {
            Debug.Log("=== Odengine V2: Field Propagation Example ===\n");

            // Create world + nodes
            var world = new Dimension();
            var cityA = world.AddNode("city_a", "City A");
            var cityB = world.AddNode("city_b", "City B");
            var ocean = world.AddNode("ocean", "Ocean");
            var cityC = world.AddNode("city_c", "City C");

            // Add edges with tags
            var edgeAB = world.AddEdge("city_a", "city_b", resistance: 1f);
            var edgeBO = world.AddEdge("city_b", "ocean", resistance: 10f);
            edgeBO.AddTag("ocean"); // Tag this as ocean
            
            var edgeOC = world.AddEdge("ocean", "city_c", resistance: 10f);
            edgeOC.AddTag("ocean");

            // Create two fields with different behaviors
            var tradeProfile = new FieldProfile(
                profileId: "trade",
                propagationRate: 0.5f,
                edgeResistanceScale: 1f,
                decayRate: 0.02f,
                minAmp: 0.01f,
                mode: ConservationMode.Diffusion // trade goods are conserved
            );
            tradeProfile.SetTagMultiplier("ocean", 5f); // oceans are hard for trade

            var cultureProfile = new FieldProfile(
                profileId: "culture",
                propagationRate: 0.8f,
                edgeResistanceScale: 0.5f,
                decayRate: 0.01f,
                minAmp: 0.01f,
                mode: ConservationMode.Radiation // culture radiates without loss
            );
            cultureProfile.SetTagMultiplier("ocean", 0.2f); // culture ignores oceans mostly

            var tradeField = world.AddField("trade", tradeProfile);
            var cultureField = world.AddField("culture", cultureProfile);

            // Set initial amplitudes
            tradeField.SetAmplitude("city_a", 100f);
            cultureField.SetAmplitude("city_a", 100f);

            Debug.Log("Initial state:");
            LogFieldState("Trade", tradeField);
            LogFieldState("Culture", cultureField);

            // Propagate for 10 ticks
            for (int tick = 0; tick < 10; tick++)
            {
                // Two-phase propagation (deterministic)
                var tradeDeltas = FieldPropagator.Step(tradeField, world.Graph, dt: 0.1f);
                tradeField.ApplyDeltas(tradeDeltas);

                var cultureDeltas = FieldPropagator.Step(cultureField, world.Graph, dt: 0.1f);
                cultureField.ApplyDeltas(cultureDeltas);
            }

            Debug.Log("\nAfter 10 ticks:");
            LogFieldState("Trade", tradeField);
            LogFieldState("Culture", cultureField);

            Debug.Log("\n=== Observations ===");
            Debug.Log("1. TRADE (Diffusion): Source reduces as it spreads. Ocean blocks most trade.");
            Debug.Log("2. CULTURE (Radiation): Source unchanged. Ocean barely slows culture spread.");
            Debug.Log("3. Same graph, different field behaviors. No special cases.");
            Debug.Log("4. Deterministic: Run this twice → identical results.\n");

            // Economy example with channeled fields
            DemonstrateEconomy(world);
        }

        void DemonstrateEconomy(Dimension world)
        {
            Debug.Log("\n=== Economy (Channeled Fields) Example ===\n");

            var availProfile = new FieldProfile("availability", 0.4f, 1f, 0.01f, 0.01f, ConservationMode.Diffusion);
            var priceProfile = new FieldProfile("price", 0.6f, 0.8f, 0.02f, 0.01f, ConservationMode.Radiation);

            var economy = new EconomyFields(availProfile, priceProfile);

            // Define items
            var water = new ItemDef("water", "Water", baseValue: 10f);
            var spice = new ItemDef("spice", "Spice", baseValue: 100f);

            // Set initial availability
            economy.Availability.SetAmplitude("city_a", water.ItemId, 50f);
            economy.Availability.SetAmplitude("city_a", spice.ItemId, 10f);

            // Set price pressure (demand)
            economy.Price.SetAmplitude("city_c", water.ItemId, 2f); // high demand in city_c
            economy.Price.SetAmplitude("city_c", spice.ItemId, 5f);

            Debug.Log("Prices before propagation:");
            Debug.Log($"  City A - Water: {economy.SamplePrice("city_a", water.ItemId, water.BaseValue):F2} credits");
            Debug.Log($"  City A - Spice: {economy.SamplePrice("city_a", spice.ItemId, spice.BaseValue):F2} credits");
            Debug.Log($"  City C - Water: {economy.SamplePrice("city_c", water.ItemId, water.BaseValue):F2} credits");
            Debug.Log($"  City C - Spice: {economy.SamplePrice("city_c", spice.ItemId, spice.BaseValue):F2} credits");

            Debug.Log("\n=== Key Points ===");
            Debug.Log("1. TWO fields (Availability + Price), not three.");
            Debug.Log("2. Channeled by itemId (scalable to 1000s of items).");
            Debug.Log("3. Demand is EMERGENT from actors pulling availability.");
            Debug.Log("4. Price = f(availability, price_pressure, base_value).");
        }

        void LogFieldState(string name, OdField field)
        {
            Debug.Log($"{name} Field:");
            foreach (var (nodeId, amp) in field.GetAllAmplitudes())
            {
                if (amp > 0.01f)
                    Debug.Log($"  {nodeId}: {amp:F2}");
            }
        }
    }
}
