using System;
using System.Collections.Generic;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.War
{
    /// <summary>
    /// WarSystem — models war as scalar field pressure at nodes.
    ///
    /// Architecture:
    ///   "war.exposure"   — per-node single channel (channelId == "x")
    ///                      logAmp > 0 → war pressure, logAmp = 0 → peace
    ///   "war.occupation" — per-node per-attacker channel (channelId == attacker nodeId or factionId)
    ///                      logAmp encodes occupation progress [0..1] in linear space
    ///
    /// War states (deterministic state machine per node):
    ///   Active   — exposure grows at ExposureGrowthRate per tick
    ///   Cooling  — ceasefire; exposure decays at CeasefireDecayRate
    ///   Ambient  — peace; exposure decays at AmbientDecayRate
    ///
    /// Front propagation uses Propagator.Step on "war.exposure" each Tick().
    ///
    /// Occupation:
    ///   DeclareOccupation registers an attacker. Each Tick accumulates progress.
    ///   When progress >= 1.0, the occupation completes and the callback fires.
    ///   Progress rate = OccupationBaseRate / (1 + stabilityResistance).
    ///   The stability value is supplied externally via SetNodeStability().
    ///
    /// No Unity dependencies, no event bus, no WorldState. Plain method calls.
    /// </summary>
    public sealed class WarSystem
    {
        // ── Exposure dynamics ───────────────────────────────────────────────
        private const float ExposureGrowthRate   = 0.05f;   // logAmp/tick at war
        private const float AmbientDecayRate     = 0.02f;   // logAmp/tick in peace
        private const float CeasefireDecayRate   = 0.06f;   // logAmp/tick cooling
        private const float ExposureEpsilon      = 0.0001f; // treat as zero

        // Single channel name for the exposure field
        // (one dimension per node — the field is effectively a node→logAmp map)
        private const string ExposureChannel = "x";

        // ── Occupation dynamics ─────────────────────────────────────────────
        private const float OccupationBaseRate          = 0.1f;  // 1/ticks for empty node
        private const float OccupationStabilityResist   = 0.2f;  // stability dampens progress

        // ── State sets ──────────────────────────────────────────────────────
        private readonly HashSet<string> _activeWarNodes  = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _coolingNodes    = new HashSet<string>(StringComparer.Ordinal);

        // nodeId → stability [0..1] (provided by game layer via SetNodeStability)
        private readonly Dictionary<string, float> _stability = new Dictionary<string, float>(StringComparer.Ordinal);

        // nodeId → (attackerId, progress [0..1])
        private readonly Dictionary<string, (string attackerId, float progress)> _occupations
            = new Dictionary<string, (string, float)>(StringComparer.Ordinal);

        // ── Fields ──────────────────────────────────────────────────────────
        private readonly Dimension _dimension;

        /// <summary>Scalar field storing war exposure per node (logAmp, single channel "x").</summary>
        public readonly ScalarField Exposure;

        // ── Callbacks ───────────────────────────────────────────────────────

        /// <summary>
        /// Fired when an occupation completes: (nodeId, attackerId).
        /// The game layer should update its control state here.
        /// </summary>
        public Action<string, string> OnOccupationComplete;

        // ── Construction ────────────────────────────────────────────────────

        /// <param name="dimension">Shared Dimension. WarSystem registers its own fields.</param>
        /// <param name="exposureProfile">FieldProfile for the exposure field.
        ///   Tip: set PropagationRate and DecayRate to 0 and drive them manually via Tick()
        ///   so that state-machine logic (active/cooling/ambient) controls decay precisely.</param>
        public WarSystem(Dimension dimension, FieldProfile exposureProfile)
        {
            _dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));
            Exposure = dimension.AddField("war.exposure", exposureProfile);
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Mark a node as actively at war. Subsequent Tick() calls will grow its exposure.
        /// Idempotent — calling twice for the same node is safe.
        /// </summary>
        public void DeclareWar(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException(nameof(nodeId));
            _coolingNodes.Remove(nodeId);
            _activeWarNodes.Add(nodeId);
        }

        /// <summary>
        /// Move a node from active war to the ceasefire-cooling state.
        /// Exposure will now decay at CeasefireDecayRate until it reaches zero.
        /// </summary>
        public void DeclareCeasefire(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException(nameof(nodeId));
            if (_activeWarNodes.Remove(nodeId))
                _coolingNodes.Add(nodeId);
        }

        /// <summary>
        /// Begin an occupation attempt on a node.
        /// Replaces any existing attempt. Progress resets to 0.
        /// </summary>
        public void DeclareOccupation(string nodeId, string attackerId)
        {
            if (string.IsNullOrEmpty(nodeId))   throw new ArgumentException(nameof(nodeId));
            if (string.IsNullOrEmpty(attackerId)) throw new ArgumentException(nameof(attackerId));
            _occupations[nodeId] = (attackerId, 0f);
        }

        /// <summary>
        /// Cancel any ongoing occupation attempt at a node.
        /// </summary>
        public void CancelOccupation(string nodeId)
        {
            _occupations.Remove(nodeId);
        }

        /// <summary>
        /// Provide the current stability [0..1] of a node.
        /// Used to resist occupation progress. Call before Tick() each frame,
        /// or once whenever the value changes. Defaults to 0 (no resistance) if unset.
        /// </summary>
        public void SetNodeStability(string nodeId, float stability)
        {
            _stability[nodeId] = Math.Clamp(stability, 0f, 1f);
        }

        /// <summary>
        /// Read current war exposure multiplier (exp(logAmp)) at a node.
        /// 1.0 = neutral/peace, values > 1.0 = war pressure present.
        /// </summary>
        public float GetExposureMultiplier(string nodeId) =>
            Exposure.GetMultiplier(nodeId, ExposureChannel);

        /// <summary>
        /// Read raw logAmp war exposure at a node.
        /// 0.0 = peace. Useful for threshold checks without exp().
        /// </summary>
        public float GetExposureLogAmp(string nodeId) =>
            Exposure.GetLogAmp(nodeId, ExposureChannel);

        /// <summary>Returns true if the node is in the active-war set.</summary>
        public bool IsAtWar(string nodeId) => _activeWarNodes.Contains(nodeId);

        /// <summary>Returns true if the node is in the ceasefire-cooling set.</summary>
        public bool IsCooling(string nodeId) => _coolingNodes.Contains(nodeId);

        /// <summary>Returns the current occupation progress [0..1] for a node, or 0 if none.</summary>
        public float GetOccupationProgress(string nodeId) =>
            _occupations.TryGetValue(nodeId, out var o) ? o.progress : 0f;

        /// <summary>Returns the current attacker id for a node occupation, or null if none.</summary>
        public string GetOccupationAttacker(string nodeId) =>
            _occupations.TryGetValue(nodeId, out var o) ? o.attackerId : null;

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance the war simulation by <paramref name="dt"/> ticks.
        ///
        /// Order:
        ///   1. Apply exposure state machine (grow / ceasefire decay / ambient decay)
        ///   2. Propagate exposure across graph edges (Propagator.Step)
        ///   3. Advance occupation progress → fire OnOccupationComplete callbacks
        /// </summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            // 1. State-machine exposure updates (sorted for determinism)
            var nodeIds = GetAllRelevantNodeIdsSorted();
            var toRemoveFromCooling = new List<string>();

            foreach (var nodeId in nodeIds)
            {
                float currentLogAmp = Exposure.GetLogAmp(nodeId, ExposureChannel);

                if (_activeWarNodes.Contains(nodeId))
                {
                    // Active war: exposure grows
                    Exposure.AddLogAmp(nodeId, ExposureChannel, ExposureGrowthRate * dt);
                }
                else if (_coolingNodes.Contains(nodeId))
                {
                    // Ceasefire: accelerated decay — clamp so logAmp never goes below zero
                    if (currentLogAmp > ExposureEpsilon)
                    {
                        float decay = MathF.Min(currentLogAmp, CeasefireDecayRate * dt);
                        Exposure.AddLogAmp(nodeId, ExposureChannel, -decay);
                    }

                    float newLogAmp = Exposure.GetLogAmp(nodeId, ExposureChannel);
                    if (newLogAmp <= ExposureEpsilon)
                        toRemoveFromCooling.Add(nodeId);
                }
                else if (currentLogAmp > ExposureEpsilon)
                {
                    // Ambient peace: slow natural decay — clamp so logAmp never goes below zero
                    float decay = MathF.Min(currentLogAmp, AmbientDecayRate * dt);
                    Exposure.AddLogAmp(nodeId, ExposureChannel, -decay);
                }
            }

            foreach (var nodeId in toRemoveFromCooling)
                _coolingNodes.Remove(nodeId);

            // 2. Propagate exposure across edges
            // (uses FieldProfile.PropagationRate and EdgeResistanceScale)
            Propagator.Step(_dimension, Exposure, dt);

            // 3. Occupation progress (sorted for determinism)
            var completedOccupations = new List<(string nodeId, string attackerId)>();
            var occupationNodeIds = new List<string>(_occupations.Keys);
            occupationNodeIds.Sort(StringComparer.Ordinal);

            foreach (var nodeId in occupationNodeIds)
            {
                if (!_occupations.TryGetValue(nodeId, out var occ)) continue;

                _stability.TryGetValue(nodeId, out float stab);
                float resistance = OccupationStabilityResist * stab;
                float rate = OccupationBaseRate / (1f + resistance);
                float newProgress = occ.progress + rate * dt;

                if (newProgress >= 1f)
                {
                    completedOccupations.Add((nodeId, occ.attackerId));
                    _occupations.Remove(nodeId);
                }
                else
                {
                    _occupations[nodeId] = (occ.attackerId, newProgress);
                }
            }

            // Fire callbacks after iteration (don't mutate during loop)
            foreach (var (nodeId, attackerId) in completedOccupations)
                OnOccupationComplete?.Invoke(nodeId, attackerId);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a deterministically sorted list of all node IDs relevant this tick:
        /// union of active-war nodes, cooling nodes, and nodes with non-zero exposure.
        /// </summary>
        private List<string> GetAllRelevantNodeIdsSorted()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in _activeWarNodes)  set.Add(n);
            foreach (var n in _coolingNodes)    set.Add(n);
            foreach (var n in Exposure.GetActiveNodeIdsSortedForChannel(ExposureChannel))
                set.Add(n);

            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
