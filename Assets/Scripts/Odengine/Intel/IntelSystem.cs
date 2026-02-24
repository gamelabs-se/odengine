using System;
using System.Collections.Generic;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Intel
{
    /// <summary>
    /// IntelSystem — models reconnaissance and intelligence coverage as a scalar field.
    ///
    /// Architecture:
    ///   "intel.coverage" — (nodeId × factionId) → logAmp
    ///     logAmp > 0  → faction has active intelligence at this node
    ///     logAmp = 0  → no coverage (neutral / unknown)
    ///     Higher logAmp → denser sensor network / more reliable intelligence
    ///
    /// How it works:
    ///   A faction deploys sensors at a node via DeploySensor().
    ///   Coverage propagates through edges (Propagator.Step) — a dense sensor
    ///   network in one node gives partial visibility into adjacent nodes, with
    ///   signal degraded by edge resistance.  The FieldProfile's DecayRate governs
    ///   how quickly coverage fades without reinforcement (patrols stop → coverage drops).
    ///
    /// Game-layer use (fog of war):
    ///   The game queries GetCoverage(nodeId, factionId) to decide what each faction
    ///   can observe.  High coverage → full visibility; near-zero → blind spot.
    ///   IntelSystem itself never reads other fields; cross-system effects belong in
    ///   CouplingRules:
    ///     intel.coverage → faction.influence  (spy networks project soft power)
    ///     intel.coverage → war.exposure       (sensors detect threat activity)
    ///
    /// No Unity dependencies, no event bus, no WorldState. Plain method calls.
    /// </summary>
    public sealed class IntelSystem
    {
        // ── Config ──────────────────────────────────────────────────────────

        private readonly IntelConfig _config;
        private readonly Dimension _dimension;

        // ── Fields ──────────────────────────────────────────────────────────

        /// <summary>
        /// Scalar field storing per-faction intelligence coverage at nodes.
        /// Channel = factionId.  logAmp encodes coverage density in log-space.
        /// </summary>
        public readonly ScalarField Coverage;

        // ── Construction ────────────────────────────────────────────────────

        /// <param name="dimension">Shared Dimension. IntelSystem registers its own field.</param>
        /// <param name="coverageProfile">FieldProfile for intel.coverage.
        ///   PropagationRate controls how far a sensor network reaches across edges.
        ///   EdgeResistanceScale controls terrain/encryption difficulty.
        ///   DecayRate controls how fast coverage drops without reinforcement.</param>
        /// <param name="config">Tuning constants. Null = defaults.</param>
        public IntelSystem(Dimension dimension, FieldProfile coverageProfile, IntelConfig config = null)
        {
            _dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));
            _config    = config ?? new IntelConfig();
            Coverage   = dimension.GetOrCreateField("intel.coverage",
                coverageProfile ?? throw new ArgumentNullException(nameof(coverageProfile)));
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Deploy sensors or scouts for <paramref name="factionId"/> at <paramref name="nodeId"/>.
        /// <paramref name="powerLogAmp"/> is added to the faction's current coverage (log-space).
        /// Can be called repeatedly to reinforce coverage each tick.
        /// </summary>
        public void DeploySensor(string nodeId, string factionId, float powerLogAmp)
        {
            if (string.IsNullOrEmpty(nodeId))    throw new ArgumentException(nameof(nodeId));
            if (string.IsNullOrEmpty(factionId)) throw new ArgumentException(nameof(factionId));
            Coverage.AddLogAmp(nodeId, factionId, powerLogAmp);
        }

        /// <summary>
        /// Raw coverage logAmp for <paramref name="factionId"/> at <paramref name="nodeId"/>.
        /// Returns 0 when the node has no active coverage entries for this faction.
        /// </summary>
        public float GetCoverage(string nodeId, string factionId)
        {
            if (string.IsNullOrEmpty(nodeId))    throw new ArgumentException(nameof(nodeId));
            if (string.IsNullOrEmpty(factionId)) throw new ArgumentException(nameof(factionId));
            return Coverage.GetLogAmp(nodeId, factionId);
        }

        /// <summary>
        /// Coverage as a linear multiplier: <c>exp(logAmp)</c>.
        /// Returns 1.0 (neutral) when there is no coverage.
        /// A multiplier > 1 means above-baseline coverage density.
        /// </summary>
        public float GetCoverageMultiplier(string nodeId, string factionId)
            => Coverage.GetMultiplier(nodeId, factionId);

        /// <summary>
        /// The faction with the highest coverage logAmp at <paramref name="nodeId"/>,
        /// or <c>null</c> if no faction has active coverage there.
        /// </summary>
        public string GetDominantObserver(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException(nameof(nodeId));
            return Coverage.GetDominantChannel(nodeId);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="factionId"/> has coverage logAmp
        /// above <see cref="IntelConfig.ActiveCoverageThreshold"/> at <paramref name="nodeId"/>.
        /// </summary>
        public bool IsTracked(string nodeId, string factionId)
            => GetCoverage(nodeId, factionId) >= _config.ActiveCoverageThreshold;

        /// <summary>
        /// All faction IDs with active coverage at <paramref name="nodeId"/>,
        /// sorted in Ordinal order for determinism.
        /// </summary>
        public IReadOnlyList<string> GetTrackingFactions(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException(nameof(nodeId));

            var channels = Coverage.GetActiveChannelIdsSortedForNode(nodeId);
            var result   = new List<string>(channels.Count);
            foreach (var ch in channels)
            {
                if (Coverage.GetLogAmp(nodeId, ch) >= _config.ActiveCoverageThreshold)
                    result.Add(ch);
            }
            return result;
        }

        /// <summary>
        /// Returns all node IDs where at least one faction has active coverage.
        /// Sorted in Ordinal order for determinism.
        /// </summary>
        public IReadOnlyList<string> GetCoveredNodeIds()
            => Coverage.GetActiveNodeIdsSorted();

        /// <summary>
        /// Total coverage logAmp at <paramref name="nodeId"/> across all factions.
        /// Useful as an aggregate "how watched is this node" signal.
        /// </summary>
        public float GetTotalCoverage(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException(nameof(nodeId));

            var channels = Coverage.GetActiveChannelIdsSortedForNode(nodeId);
            float sum = 0f;
            foreach (var ch in channels)
                sum += Coverage.GetLogAmp(nodeId, ch);
            return sum;
        }

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance intelligence dynamics by <paramref name="deltaTime"/> seconds.
        /// Propagator.Step handles coverage spreading to adjacent nodes and natural decay.
        /// </summary>
        public void Tick(float deltaTime)
            => Propagator.Step(_dimension, Coverage, deltaTime);
    }
}
