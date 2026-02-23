using System;
using System.Collections.Generic;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Faction
{
    /// <summary>
    /// FactionSystem — models territorial control and political presence as scalar fields.
    ///
    /// Three fields, all indexed (nodeId, factionId → logAmp):
    ///
    ///   faction.presence  — military / police / administrative footprint.
    ///                       The faction with the highest positive logAmp at a node
    ///                       is considered the dominant (territorial controller).
    ///                       logAmp = 0 → neutral baseline (no footprint).
    ///
    ///   faction.influence — cultural / economic / soft-power reach.
    ///                       Can extend beyond hard presence — trade networks,
    ///                       propaganda, diplomatic ties.
    ///
    ///   faction.stability — governance quality at a node under a given faction.
    ///                       Driven by impulses from the game layer; can be coupled
    ///                       from war.exposure via CouplingEvaluator.
    ///
    /// All three propagate via Propagator.Step each Tick().
    /// Their FieldProfiles control propagation rate, edge resistance, and decay.
    ///
    /// Territorial control is derived on observation — never stored:
    ///   GetDominantFaction(nodeId) → argmax over presence channels
    ///   IsContested(nodeId)        → top two factions within gapThreshold of each other
    ///   GetTotalPresenceLogAmp(nodeId) → power-vacuum detector (low = ungoverned)
    ///
    /// OnDominanceChanged fires when the argmax result flips for any node.
    ///
    /// Game-layer pattern:
    ///   // Bootstrap: inject initial presence
    ///   factions.AddPresence("earth", "empire_red", 2.0f);
    ///
    ///   // Each tick: couple external pressures in, then propagate
    ///   factions.AddPresence("frontline", "empire_red", -warImpulse);   // war erodes presence
    ///   factions.Tick(dt);
    ///
    ///   // Read dominance
    ///   string controller = factions.GetDominantFaction("earth");
    /// </summary>
    public sealed class FactionSystem
    {
        // ── Fields ─────────────────────────────────────────────────────────────
        private readonly Dimension _dimension;

        /// <summary>Military / administrative footprint. Controls territorial dominance.</summary>
        public readonly ScalarField Presence;

        /// <summary>Cultural / economic soft-power reach.</summary>
        public readonly ScalarField Influence;

        /// <summary>Governance quality at a node under a faction's control.</summary>
        public readonly ScalarField Stability;

        // ── Dominance tracking ──────────────────────────────────────────────────
        // Last-known dominant faction per node — used only to detect changes for callbacks.
        // Not a source of truth; dominance is always re-derived from Presence.
        private readonly Dictionary<string, string> _lastDominant
            = new Dictionary<string, string>(StringComparer.Ordinal);

        // ── Callbacks ───────────────────────────────────────────────────────────

        /// <summary>
        /// Fired at the end of Tick() when the dominant faction at a node changes.
        /// Parameters: (nodeId, previousDominant, newDominant).
        /// previousDominant is null when a node first gains a dominant faction.
        /// newDominant is null when all presence decays and the node becomes ungoverned.
        /// </summary>
        public Action<string, string, string> OnDominanceChanged;

        // ── Construction ────────────────────────────────────────────────────────

        /// <summary>
        /// Create a FactionSystem, registering three fields in the shared Dimension.
        ///
        /// Recommended FieldProfile settings:
        ///   presence  — moderate PropagationRate (military spread is slow), high EdgeResistanceScale
        ///   influence — higher PropagationRate (soft power spreads faster along trade edges)
        ///   stability — low PropagationRate; driven mainly by coupling rules, not raw propagation
        /// </summary>
        public FactionSystem(Dimension dimension,
            FieldProfile presenceProfile,
            FieldProfile influenceProfile,
            FieldProfile stabilityProfile)
        {
            _dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));
            // GetOrCreateField: safe for both fresh start and post-snapshot resume.
            // On resume, RestoreFields runs first and creates the field with the saved profile;
            // subsequent GetOrCreateField calls return the existing field unchanged.
            Presence = dimension.GetOrCreateField("faction.presence", presenceProfile ?? throw new ArgumentNullException(nameof(presenceProfile)));
            Influence = dimension.GetOrCreateField("faction.influence", influenceProfile ?? throw new ArgumentNullException(nameof(influenceProfile)));
            Stability = dimension.GetOrCreateField("faction.stability", stabilityProfile ?? throw new ArgumentNullException(nameof(stabilityProfile)));
        }

        // ── Impulse API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Inject a logAmp delta into the presence field at a node for a faction.
        /// Positive delta grows presence; negative delta erodes it (war damage, retreat).
        /// </summary>
        public void AddPresence(string nodeId, string factionId, float deltaLogAmp)
            => Presence.AddLogAmp(nodeId, factionId, deltaLogAmp);

        /// <summary>
        /// Inject a logAmp delta into the influence field at a node for a faction.
        /// </summary>
        public void AddInfluence(string nodeId, string factionId, float deltaLogAmp)
            => Influence.AddLogAmp(nodeId, factionId, deltaLogAmp);

        /// <summary>
        /// Inject a logAmp delta into the stability field at a node for a faction.
        /// </summary>
        public void AddStability(string nodeId, string factionId, float deltaLogAmp)
            => Stability.AddLogAmp(nodeId, factionId, deltaLogAmp);

        // ── Observation API ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the factionId with the highest positive presence logAmp at nodeId,
        /// or null if no faction has any positive presence there (ungoverned / power vacuum).
        /// Derived on observation — never stored.
        /// </summary>
        public string GetDominantFaction(string nodeId) => Presence.GetDominantChannel(nodeId);

        /// <summary>
        /// True when two or more factions have positive presence at the node and the gap
        /// between the top two is less than <paramref name="gapThreshold"/> logAmp.
        /// Default threshold of 0.3 means the leading faction has less than ~35% more
        /// presence multiplier than the second — genuinely contested.
        /// </summary>
        public bool IsContested(string nodeId, float gapThreshold = 0.3f)
        {
            var channels = Presence.GetActiveChannelIdsSortedForNode(nodeId);
            float best = float.NegativeInfinity;
            float second = float.NegativeInfinity;

            foreach (var ch in channels)
            {
                float v = Presence.GetLogAmp(nodeId, ch);
                if (v > best) { second = best; best = v; }
                else if (v > second) second = v;
            }

            // Both top two must be positive (above neutral baseline) and close together
            return best > 0f && second > 0f && (best - second) < gapThreshold;
        }

        /// <summary>
        /// Sum of all faction presence logAmps at a node.
        /// Low value ≈ power vacuum (ungoverned space). High value = well-administered.
        /// </summary>
        public float GetTotalPresenceLogAmp(string nodeId)
        {
            float total = 0f;
            foreach (var ch in Presence.GetActiveChannelIdsSortedForNode(nodeId))
                total += Presence.GetLogAmp(nodeId, ch);
            return total;
        }

        /// <summary>Presence multiplier (exp(logAmp)) for a faction at a node. 1.0 = neutral baseline.</summary>
        public float GetPresenceMultiplier(string nodeId, string factionId)
            => Presence.GetMultiplier(nodeId, factionId);

        /// <summary>Influence multiplier (exp(logAmp)) for a faction at a node.</summary>
        public float GetInfluenceMultiplier(string nodeId, string factionId)
            => Influence.GetMultiplier(nodeId, factionId);

        /// <summary>Stability multiplier (exp(logAmp)) for a faction at a node.</summary>
        public float GetStabilityMultiplier(string nodeId, string factionId)
            => Stability.GetMultiplier(nodeId, factionId);

        // ── Tick ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance the faction simulation by <paramref name="dt"/> ticks.
        ///
        /// Order:
        ///   1. Propagator.Step on Presence  (spread along graph edges)
        ///   2. Propagator.Step on Influence
        ///   3. Propagator.Step on Stability
        ///   4. Check dominance changes → fire OnDominanceChanged callbacks
        /// </summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            Propagator.Step(_dimension, Presence, dt);
            Propagator.Step(_dimension, Influence, dt);
            Propagator.Step(_dimension, Stability, dt);

            CheckDominanceChanges();
        }

        // ── Snapshot support ──────────────────────────────────────────────────────

        // Field state (faction.presence / .influence / .stability): serialized automatically
        // by SnapshotWriter. _lastDominant is a reconstructable cache — NOT serialized;
        // rebuilt by PostLoad() so the dominance-change callback fires correctly after resume.
        // OnDominanceChanged: NOT serialized — re-register after load.
        public void PostLoad()
        {
            _lastDominant.Clear();
            foreach (var nodeId in Presence.GetActiveNodeIdsSorted())
            {
                string dominant = Presence.GetDominantChannel(nodeId);
                if (dominant != null) _lastDominant[nodeId] = dominant;
            }
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void CheckDominanceChanges()
        {
            // Union of: nodes with active presence now + nodes we previously tracked
            var activeNodes = Presence.GetActiveNodeIdsSorted();
            var nodeSet = new HashSet<string>(activeNodes, StringComparer.Ordinal);
            foreach (var n in _lastDominant.Keys) nodeSet.Add(n);

            var allNodes = new List<string>(nodeSet);
            allNodes.Sort(StringComparer.Ordinal);

            // Collect changes before firing (don't mutate _lastDominant during iteration)
            var changes = new List<(string nodeId, string oldDom, string newDom)>();

            foreach (var nodeId in allNodes)
            {
                string newDom = Presence.GetDominantChannel(nodeId);
                _lastDominant.TryGetValue(nodeId, out string oldDom);

                if (newDom != oldDom)
                    changes.Add((nodeId, oldDom, newDom));
            }

            // Apply updates then fire
            foreach (var (nodeId, oldDom, newDom) in changes)
            {
                if (newDom == null) _lastDominant.Remove(nodeId);
                else _lastDominant[nodeId] = newDom;

                OnDominanceChanged?.Invoke(nodeId, oldDom, newDom);
            }
        }
    }
}
