using System;
using System.Collections.Generic;

namespace Odengine.Faction
{
    /// <summary>
    /// FactionSystem — derives political health for factions from node-level inputs.
    ///
    /// No Dimension, no ScalarField, no Intent plumbing.
    /// Faction aggregates are computed (not propagated), so plain dictionaries suffice.
    ///
    /// Game-layer bridge pattern each tick:
    ///   foreach node:
    ///     factions.SetNodeStability(nodeId, yourStabilityValue);
    ///     factions.SetNodeWarExposure(nodeId, war.GetExposureLogAmp(nodeId));
    ///   factions.Tick(dt);
    ///
    /// Tick() order:
    ///   1. RecomputeAggregates  — mean stability + war exposure per faction
    ///   2. UpdatePoliticalHealth — smooth PoliticalStability, accumulate WarExhaustion
    ///   3. Fire threshold callbacks
    /// </summary>
    public sealed class FactionSystem
    {
        // ── Political health constants ─────────────────────────────────────────
        private const float StabilitySmoothingRate  = 0.05f;  // exp-smoothing rate per tick
        private const float WarExhaustionGainRate   = 0.01f;  // exhaustion gain per unit of average war exposure per tick
        private const float WarExhaustionDecayRate  = 0.005f; // exhaustion decay per tick when at peace

        /// <summary>War exposure logAmp above which WarExhaustion grows.</summary>
        private const float WarExposureThreshold = 0.1f;

        // ── Publicly readable threshold constants ─────────────────────────────
        public const float StabilityCrisisThreshold  = 0.3f;
        public const float StabilityStableThreshold  = 0.7f;

        // ── Internal state ─────────────────────────────────────────────────────
        private readonly Dictionary<string, FactionState> _factions
            = new Dictionary<string, FactionState>(StringComparer.Ordinal);

        // nodeId → factionId
        private readonly Dictionary<string, string> _controllers
            = new Dictionary<string, string>(StringComparer.Ordinal);

        // nodeId → stability [0..1] — default 1.0 (fully stable) when not set
        private readonly Dictionary<string, float> _nodeStability
            = new Dictionary<string, float>(StringComparer.Ordinal);

        // nodeId → war exposure logAmp — default 0 (peace) when not set
        private readonly Dictionary<string, float> _nodeWarExposure
            = new Dictionary<string, float>(StringComparer.Ordinal);

        // ── Callbacks ──────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when a node's controller changes: (nodeId, newFactionId).
        /// Also fired by TransferControl(); NOT fired by SetNodeController().
        /// </summary>
        public Action<string, string> OnControlTransferred;

        /// <summary>
        /// Fired when a faction's PoliticalStability crosses below StabilityCrisisThreshold.
        /// </summary>
        public Action<string> OnFactionCollapse;

        /// <summary>
        /// Fired when a faction's PoliticalStability crosses above StabilityStableThreshold.
        /// </summary>
        public Action<string> OnFactionStabilize;

        // ── Faction registration ───────────────────────────────────────────────

        /// <summary>
        /// Register a faction. Idempotent — safe to call multiple times.
        /// </summary>
        public void RegisterFaction(string factionId)
        {
            if (string.IsNullOrEmpty(factionId))
                throw new ArgumentException("FactionId cannot be null or empty", nameof(factionId));

            if (!_factions.ContainsKey(factionId))
                _factions[factionId] = new FactionState(factionId);
        }

        /// <summary>Returns true if the faction has been registered.</summary>
        public bool HasFaction(string factionId) =>
            !string.IsNullOrEmpty(factionId) && _factions.ContainsKey(factionId);

        /// <summary>Returns the FactionState for a registered faction, or null if unknown.</summary>
        public FactionState GetFaction(string factionId) =>
            _factions.TryGetValue(factionId ?? "", out var s) ? s : null;

        /// <summary>All registered faction IDs in deterministic Ordinal-sorted order.</summary>
        public IReadOnlyList<string> GetFactionIdsSorted()
        {
            var list = new List<string>(_factions.Keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // ── Node control API ───────────────────────────────────────────────────

        /// <summary>
        /// Silently assign a faction as controller of a node (world-setup use).
        /// Does NOT fire OnControlTransferred. Auto-registers the faction.
        /// </summary>
        public void SetNodeController(string nodeId, string factionId)
        {
            ValidateId(nodeId,    nameof(nodeId));
            ValidateId(factionId, nameof(factionId));
            RegisterFaction(factionId);
            _controllers[nodeId] = factionId;
        }

        /// <summary>Returns the faction currently controlling this node, or null if uncontrolled.</summary>
        public string GetNodeController(string nodeId) =>
            _controllers.TryGetValue(nodeId ?? "", out var f) ? f : null;

        /// <summary>
        /// Transfer control of a node to a new faction. Fires OnControlTransferred.
        /// Auto-registers the target faction.
        /// </summary>
        public void TransferControl(string nodeId, string toFactionId)
        {
            ValidateId(nodeId,      nameof(nodeId));
            ValidateId(toFactionId, nameof(toFactionId));
            RegisterFaction(toFactionId);
            _controllers[nodeId] = toFactionId;
            OnControlTransferred?.Invoke(nodeId, toFactionId);
        }

        /// <summary>Remove control assignment from a node (makes it uncontrolled).</summary>
        public void ClearNodeController(string nodeId) => _controllers.Remove(nodeId ?? "");

        // ── Node input setters ─────────────────────────────────────────────────

        /// <summary>
        /// Set the current node stability [0..1] for aggregation.
        /// Default when unset: 1.0 (fully stable — neutral baseline).
        /// </summary>
        public void SetNodeStability(string nodeId, float stability)
        {
            ValidateId(nodeId, nameof(nodeId));
            _nodeStability[nodeId] = Math.Clamp(stability, 0f, 1f);
        }

        /// <summary>
        /// Set the current war exposure logAmp at a node for aggregation.
        /// Typically: war.GetExposureLogAmp(nodeId).
        /// Default when unset: 0 (no war — neutral baseline).
        /// </summary>
        public void SetNodeWarExposure(string nodeId, float warExposureLogAmp)
        {
            ValidateId(nodeId, nameof(nodeId));
            _nodeWarExposure[nodeId] = warExposureLogAmp;
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        /// <summary>Advance faction simulation by <paramref name="dt"/> ticks.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            RecomputeAggregates();
            UpdatePoliticalHealth(dt);
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void RecomputeAggregates()
        {
            // Reset all faction counts — sorted for determinism
            foreach (var factionId in GetFactionIdsSorted())
            {
                var f = _factions[factionId];
                f.ControlledNodeCount = 0;
                f.AverageStability    = 0f;
                f.AverageWarExposure  = 0f;
            }

            // Accumulators
            var stabilitySum   = new Dictionary<string, float>(StringComparer.Ordinal);
            var warExposureSum = new Dictionary<string, float>(StringComparer.Ordinal);
            var nodeCount      = new Dictionary<string, int>(StringComparer.Ordinal);

            // Iterate all controlled nodes in sorted order
            var controlledNodes = new List<string>(_controllers.Keys);
            controlledNodes.Sort(StringComparer.Ordinal);

            foreach (var nodeId in controlledNodes)
            {
                string factionId = _controllers[nodeId];
                if (!_factions.TryGetValue(factionId, out var faction)) continue;

                faction.ControlledNodeCount++;

                // Stability defaults to 1.0 (fully stable) when game layer hasn't set it
                float stab   = _nodeStability.TryGetValue(nodeId, out float s) ? s : 1f;
                // War exposure defaults to 0.0 (peace) when game layer hasn't set it
                float warExp = _nodeWarExposure.TryGetValue(nodeId, out float w) ? w : 0f;

                stabilitySum.TryGetValue(factionId, out float sSum);
                warExposureSum.TryGetValue(factionId, out float wSum);
                nodeCount.TryGetValue(factionId, out int n);

                stabilitySum[factionId]   = sSum + stab;
                warExposureSum[factionId] = wSum + warExp;
                nodeCount[factionId]      = n + 1;
            }

            // Finalise averages — sorted for determinism
            foreach (var factionId in GetFactionIdsSorted())
            {
                var f = _factions[factionId];
                if (nodeCount.TryGetValue(factionId, out int n) && n > 0)
                {
                    f.AverageStability   = stabilitySum[factionId]   / n;
                    f.AverageWarExposure = warExposureSum[factionId] / n;
                }
                else
                {
                    // No controlled nodes: feed back current political stability so it stays put
                    f.AverageStability   = f.PoliticalStability;
                    f.AverageWarExposure = 0f;
                }
            }
        }

        private void UpdatePoliticalHealth(float dt)
        {
            // Collect crossing events to fire after iteration (don't mutate while iterating)
            var collapseEvents  = new List<string>();
            var stabilizeEvents = new List<string>();

            foreach (var factionId in GetFactionIdsSorted())
            {
                var f = _factions[factionId];

                float oldStability  = f.PoliticalStability;

                // ── Political stability: exponential smoothing toward AverageStability ──
                // k is the delta-time-correct blending factor: approaches 1 as dt grows
                float k = 1f - MathF.Exp(-StabilitySmoothingRate * dt);
                f.PoliticalStability += (f.AverageStability - f.PoliticalStability) * k;
                f.PoliticalStability  = Math.Clamp(f.PoliticalStability, 0f, 1f);

                // ── War exhaustion ─────────────────────────────────────────────────────
                if (f.AverageWarExposure > WarExposureThreshold)
                    f.WarExhaustion += WarExhaustionGainRate * f.AverageWarExposure * dt;
                else
                    f.WarExhaustion = Math.Max(0f, f.WarExhaustion - WarExhaustionDecayRate * dt);

                f.WarExhaustion = Math.Clamp(f.WarExhaustion, 0f, 1f);

                // ── IsCollapsing flag ──────────────────────────────────────────────────
                bool wasCollapsing = f.IsCollapsing;
                f.IsCollapsing = f.PoliticalStability < StabilityCrisisThreshold;

                // ── Threshold crossings ────────────────────────────────────────────────
                if (!wasCollapsing && f.IsCollapsing)
                    collapseEvents.Add(factionId);

                if (oldStability < StabilityStableThreshold &&
                    f.PoliticalStability >= StabilityStableThreshold)
                    stabilizeEvents.Add(factionId);
            }

            // Fire callbacks outside the iteration loop
            foreach (var id in collapseEvents)  OnFactionCollapse?.Invoke(id);
            foreach (var id in stabilizeEvents) OnFactionStabilize?.Invoke(id);
        }

        private static void ValidateId(string value, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException($"{paramName} cannot be null or empty", paramName);
        }
    }
}
