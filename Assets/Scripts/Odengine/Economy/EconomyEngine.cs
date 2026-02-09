using System;
using System.Collections.Generic;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Economy
{
    /// <summary>
    /// Economy engine using lazy-virtualized scalar fields.
    /// 
    /// Core concept:
    /// - Availability field has base amplitude (general market strength)
    /// - Individual items are channels that deviate from field when needed
    /// - Price derived from: BaseValue * f(channelAmp OR fieldAmp, demand, supply)
    /// </summary>
    public sealed class EconomyEngine : IChannelProfileProvider
    {
        private readonly Dimension _dimension;

        // Domain fields
        public readonly ScalarField Availability;
        public readonly ScalarField Price;

        // Item registry
        private readonly Dictionary<string, ItemDef> _items = new(StringComparer.Ordinal);

        // Optional per-item overrides
        private readonly Dictionary<string, ChannelProfileOverride> _overrides = new(StringComparer.Ordinal);

        public EconomyEngine(Dimension dimension)
        {
            _dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));

            var profile = new FieldProfile("economy")
            {
                PropagationRate = 0.3f,
                EdgeResistanceScale = 0.5f,
                DecayRate = 0.02f,
                MinAmp = 0.001f,
                Mode = ConservationMode.Radiation
            };

            var priceProfile = new FieldProfile("economy.price")
            {
                PropagationRate = 0.1f,
                EdgeResistanceScale = 0.3f,
                DecayRate = 0.05f,
                MinAmp = 0.001f,
                Mode = ConservationMode.Radiation
            };

            Availability = dimension.AddScalarField("economy.availability", profile);
            Price = dimension.AddScalarField("economy.price", priceProfile);

            Availability.ChannelProfileProvider = this;
            Price.ChannelProfileProvider = this;

            // Configure lazy virtualization thresholds
            Availability.MergeThreshold = 0.1f;
            Availability.NormalizationThreshold = 2.0f;
            Availability.NormalizationRate = 0.1f;

            Price.MergeThreshold = 0.05f;
            Price.NormalizationThreshold = 1.5f;
            Price.NormalizationRate = 0.15f;
        }

        public void RegisterItem(ItemDef item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _items[item.ItemId] = item;
        }

        public void SetItemOverride(string itemId, ChannelProfileOverride ov)
        {
            _overrides[itemId] = ov;
        }

        public ChannelProfileOverride GetOverride(string channelId)
        {
            return _overrides.TryGetValue(channelId, out var ov) ? ov : null;
        }

        /// <summary>
        /// Get price for item at node.
        /// Uses channel amp if tracked, otherwise field amp (lazy fallback).
        /// </summary>
        public float GetPrice(string itemId, string nodeId)
        {
            if (!_items.TryGetValue(itemId, out var item))
                throw new ArgumentException($"Unknown item: {itemId}");

            // Get availability (channel-specific or field fallback)
            float availability = Availability.For(itemId).GetAmp(nodeId);

            // If no availability, return base price
            if (availability < 0.01f)
                return item.BaseValue;

            // Simple scarcity formula: lower availability = higher price
            // This can be made more sophisticated later
            float scarcityMultiplier = 1f / (1f + availability);

            return item.BaseValue * scarcityMultiplier;
        }

        /// <summary>
        /// Apply trade intent: buying decreases availability, selling increases it
        /// </summary>
        public void ApplyTrade(string itemId, string nodeId, float quantity)
        {
            if (!_items.ContainsKey(itemId))
                throw new ArgumentException($"Unknown item: {itemId}");

            // Buying (positive quantity) reduces availability
            // Selling (negative quantity) increases availability
            float availabilityDelta = -quantity * 0.1f; // Scale factor

            Availability.For(itemId).AddAmp(nodeId, availabilityDelta);

            // This automatically realizes the channel if deviation exceeds threshold
        }

        /// <summary>
        /// Tick: propagate fields and process virtualization
        /// </summary>
        public void Tick(float dt)
        {
            // Propagate base fields (this also propagates realized channels)
            FieldPropagator.Step(Availability, _dimension.Graph, dt);
            FieldPropagator.Step(Price, _dimension.Graph, dt);

            // Process merge/split logic
            Availability.ProcessVirtualization();
            Price.ProcessVirtualization();
        }

        /// <summary>
        /// Set base market strength at node (affects all items unless channel realized)
        /// </summary>
        public void SetMarketStrength(string nodeId, float strength)
        {
            Availability.SetFieldAmp(nodeId, strength);
        }

        /// <summary>
        /// Get item def (for game layer)
        /// </summary>
        public ItemDef GetItem(string itemId)
        {
            return _items.TryGetValue(itemId, out var item) ? item : null;
        }

        /// <summary>
        /// Get all registered items
        /// </summary>
        public IReadOnlyDictionary<string, ItemDef> GetAllItems() => _items;
    }
}
