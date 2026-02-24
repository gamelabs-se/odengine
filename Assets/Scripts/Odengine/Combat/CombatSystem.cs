using System;
using System.Collections.Generic;
using System.Linq;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Combat
{
    /// <summary>
    /// CombatSystem — models combat as per-faction field intensity at nodes.
    ///
    /// Architecture:
    ///   "combat.intensity" — (nodeId × factionId) → logAmp
    ///     logAmp > 0 → faction has fighting forces present
    ///     Higher logAmp → more powerful / numerous forces
    ///
    /// Attrition model (double-buffered, deterministic):
    ///   Each tick, at every active node, each faction loses logAmp proportional to
    ///   the combined logAmp of all OTHER factions present at that node:
    ///
    ///     delta = -sum(opponents) × AttritionRate × dt
    ///
    ///   All deltas are accumulated first, then applied in sorted (nodeId, factionId) order.
    ///   The FieldProfile's DecayRate provides natural peacetime dissipation via Propagator.
    ///
    /// Cross-system effects:
    ///   CombatSystem does NOT directly read or write War/Economy/Faction fields.
    ///   Those linkages belong in CouplingRules at the game/runner layer:
    ///     combat.intensity → war.exposure      (combat drives war)
    ///     combat.intensity → faction.presence  (losses erode faction strength)
    ///     combat.intensity → economy.availability (disrupts supply)
    ///
    /// No Unity dependencies, no event bus, no Intent system. Plain method calls.
    /// </summary>
    public sealed class CombatSystem
    {
        // ── Config ──────────────────────────────────────────────────────────

        private readonly CombatConfig _config;
        private readonly Dimension _dimension;

        // ── Fields ──────────────────────────────────────────────────────────

        /// <summary>
        /// Scalar field storing per-faction combat intensity at nodes.
        /// Channel = factionId.  logAmp = log-scale fighting strength.
        /// </summary>
        public readonly ScalarField Intensity;

        // ── Construction ────────────────────────────────────────────────────

        /// <param name="dimension">Shared Dimension. CombatSystem registers its own field.</param>
        /// <param name="intensityProfile">FieldProfile for combat.intensity.
        ///   DecayRate governs peacetime dissipation.
        ///   PropagationRate governs how combat spreads to adjacent nodes.</param>
        /// <param name="config">Tuning constants. Null = defaults.</param>
        public CombatSystem(Dimension dimension, FieldProfile intensityProfile, CombatConfig config = null)
        {
            _dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));
            _config    = config ?? new CombatConfig();
            Intensity  = dimension.GetOrCreateField("combat.intensity",
                intensityProfile ?? throw new ArgumentNullException(nameof(intensityProfile)));
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Commit fighting forces for <paramref name="factionId"/> at <paramref name="nodeId"/>.
        /// <paramref name="powerLogAmp"/> is added to the faction's current intensity (log-space).
        /// Call each tick the faction chooses to engage, or once for a one-shot strike.
        /// </summary>
        public void CommitForce(string nodeId, string factionId, float powerLogAmp)
        {
            if (string.IsNullOrEmpty(nodeId))    throw new ArgumentException(nameof(nodeId));
            if (string.IsNullOrEmpty(factionId)) throw new ArgumentException(nameof(factionId));
            Intensity.AddLogAmp(nodeId, factionId, powerLogAmp);
        }

        /// <summary>
        /// The faction with the highest intensity logAmp at <paramref name="nodeId"/>,
        /// or <c>null</c> if there is no active combat at that node.
        /// </summary>
        public string GetDominantFaction(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException(nameof(nodeId));
            return Intensity.GetDominantChannel(nodeId);
        }

        /// <summary>
        /// Total combat intensity at <paramref name="nodeId"/> — sum of all active faction logAmps.
        /// Returns 0 if the node has no active combat.
        /// </summary>
        public float GetIntensity(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException(nameof(nodeId));

            var channels = Intensity.GetActiveChannelIdsSortedForNode(nodeId);
            float sum = 0f;
            foreach (var ch in channels)
                sum += Intensity.GetLogAmp(nodeId, ch);
            return sum;
        }

        /// <summary>
        /// Returns all node IDs where at least one faction has active intensity (above threshold).
        /// Sorted in Ordinal order for determinism.
        /// </summary>
        public IReadOnlyList<string> GetActiveNodeIds()
            => Intensity.GetActiveNodeIdsSorted();

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance combat by <paramref name="deltaTime"/> seconds.
        ///
        /// Step 1 — Accumulate attrition deltas (reads only, double-buffered):
        ///   For each active node, for each faction:
        ///     delta = -sum(all other factions' logAmp) × AttritionRate × deltaTime
        ///
        /// Step 2 — Apply deltas sorted by (nodeId, factionId) for determinism.
        ///
        /// Step 3 — Propagator.Step spreads intensity to adjacent nodes and applies
        ///           the FieldProfile's natural decay rate.
        /// </summary>
        public void Tick(float deltaTime)
        {
            ApplyAttrition(deltaTime);
            Propagator.Step(_dimension, Intensity, deltaTime);
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void ApplyAttrition(float deltaTime)
        {
            // ── Phase 1: accumulate ────────────────────────────────────────
            // Key: (nodeId, factionId)  Value: logAmp delta to apply
            var deltas = new Dictionary<(string nodeId, string factionId), float>();

            var activeNodes = Intensity.GetActiveNodeIdsSorted();
            foreach (var nodeId in activeNodes)
            {
                var factions = Intensity.GetActiveChannelIdsSortedForNode(nodeId);
                if (factions.Count < 2)
                    continue; // Need at least two opposing factions for attrition

                // Read all current logAmps at this node
                float nodeTotal = 0f;
                var logAmps = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var fid in factions)
                {
                    float amp = Intensity.GetLogAmp(nodeId, fid);
                    if (amp < _config.ActiveThreshold)
                        continue; // Below threshold — treated as inactive
                    logAmps[fid] = amp;
                    nodeTotal += amp;
                }

                if (logAmps.Count < 2)
                    continue;

                // Each faction loses logAmp proportional to all opponents combined
                foreach (var (fid, ownAmp) in logAmps)
                {
                    float opponentTotal = nodeTotal - ownAmp;
                    float delta = -opponentTotal * _config.AttritionRate * deltaTime;
                    deltas[(nodeId, fid)] = delta;
                }
            }

            // ── Phase 2: apply sorted by (nodeId, factionId) ──────────────
            foreach (var key in deltas.Keys
                .OrderBy(k => k.nodeId,   StringComparer.Ordinal)
                .ThenBy(k => k.factionId, StringComparer.Ordinal))
            {
                Intensity.AddLogAmp(key.nodeId, key.factionId, deltas[key]);
            }
        }
    }
}
