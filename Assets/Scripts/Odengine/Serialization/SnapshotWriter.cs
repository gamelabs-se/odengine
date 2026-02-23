using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Odengine.Core;
using Odengine.Fields;
using Odengine.Graph;

namespace Odengine.Serialization
{
    /// <summary>
    /// Writes Odengine snapshots to a compact deterministic binary format.
    ///
    /// Format overview (little-endian throughout):
    ///   Header:      magic(4) schema_version(2) snapshot_type(1) tick(8) sim_time(8)
    ///                created_utc_ms(8) parent_tick(8) delta_chain_depth(2) engine_version(str)
    ///   String pool: count(4) [len(4) bytes…]…
    ///   Graph:       present(1) [node_count(4) [id_idx(4) name_idx(4)]…
    ///                            edge_count(4) [from(4) to(4) res(4) tag_count(4) [tag_idx(4)]…]…]
    ///   Fields:      count(4) [field_id_idx(4) profile(7×float) entry_count(4)
    ///                          [node_idx(4) ch_idx(4) log_amp(4)]…]…
    ///   Blobs:       count(4) [sys_id_idx(4) schema_ver(2) payload_len(4) payload]…
    ///
    /// String pool is Ordinal-sorted before writing so output is deterministic.
    /// Delta entries with log_amp == 0.0f mean "entry removed since parent snapshot".
    /// </summary>
    public sealed class SnapshotWriter
    {
        internal const uint   Magic         = 0x4F44534E; // 'ODSN'
        internal const ushort SchemaVersion = 1;

        private readonly SnapshotConfig _config;

        public SnapshotWriter(SnapshotConfig config = null)
        {
            _config = config ?? SnapshotConfig.Default;
        }

        // ── Public write API ───────────────────────────────────────────────────

        /// <summary>
        /// Write a Full snapshot — complete, self-contained, no system blobs.
        /// Use for per-tick recording and postmortem analysis.
        /// </summary>
        public byte[] WriteFull(Dimension dimension, ulong tick, double simTime)
        {
            if (dimension == null) throw new ArgumentNullException(nameof(dimension));
            return Write(dimension, SnapshotType.Full, tick, simTime,
                parentTick: 0, chainDepth: 0,
                previousEntries: null, participants: null);
        }

        /// <summary>
        /// Write a Checkpoint snapshot — full state + all system blobs.
        /// Required for load-and-resume. Use at explicit save points.
        /// </summary>
        public byte[] WriteCheckpoint(Dimension dimension, ulong tick, double simTime,
            IReadOnlyList<ISnapshotParticipant> participants)
        {
            if (dimension == null) throw new ArgumentNullException(nameof(dimension));
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            return Write(dimension, SnapshotType.Checkpoint, tick, simTime,
                parentTick: 0, chainDepth: 0,
                previousEntries: null, participants: participants);
        }

        /// <summary>
        /// Write a Delta snapshot — only entries changed since the previous snapshot.
        /// Entries with log_amp == 0 in the output mean "removed since parent".
        /// previousSnapshotBytes must be a Full, Delta, or Checkpoint from the parent tick.
        /// </summary>
        public byte[] WriteDelta(Dimension currentDimension, byte[] previousSnapshotBytes,
            ulong tick, double simTime, ulong parentTick, ushort chainDepth)
        {
            if (currentDimension == null) throw new ArgumentNullException(nameof(currentDimension));
            if (previousSnapshotBytes == null) throw new ArgumentNullException(nameof(previousSnapshotBytes));

            var prevSnap = new SnapshotReader(_config).Read(previousSnapshotBytes);
            var previousEntries = BuildPreviousEntryMap(prevSnap);

            return Write(currentDimension, SnapshotType.Delta, tick, simTime,
                parentTick, chainDepth, previousEntries, participants: null);
        }

        // ── Core ──────────────────────────────────────────────────────────────

        private byte[] Write(
            Dimension dimension,
            SnapshotType snapType,
            ulong tick,
            double simTime,
            ulong parentTick,
            ushort chainDepth,
            Dictionary<(string fieldId, string nodeId, string channelId), float> previousEntries,
            IReadOnlyList<ISnapshotParticipant> participants)
        {
            // Guard duplicate system IDs up front
            if (participants != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in participants)
                    if (!seen.Add(p.SystemId))
                        throw new InvalidOperationException(
                            $"Duplicate SystemId '{p.SystemId}' in participants list.");
            }

            // Build Ordinal-sorted string pool for determinism.
            // previousEntries must be included so that removed-entry strings (node/channel IDs
            // that no longer appear in any active field) are still in the pool for delta writes.
            string[] pool = BuildPool(dimension, participants, previousEntries);
            var poolIdx = new Dictionary<string, uint>(pool.Length, StringComparer.Ordinal);
            for (uint i = 0; i < pool.Length; i++) poolIdx[pool[i]] = i;

            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            // ── Header ────────────────────────────────────────────────────────
            w.Write(Magic);
            w.Write(SchemaVersion);
            w.Write((byte)snapType);
            w.Write(tick);
            w.Write(simTime);
            w.Write((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            w.Write(parentTick);
            w.Write(chainDepth);
            WriteStr(w, _config.EngineVersion);

            // ── String pool ───────────────────────────────────────────────────
            w.Write(pool.Length);
            foreach (var s in pool) WriteStr(w, s);

            // ── Graph ─────────────────────────────────────────────────────────
            bool writeGraph = snapType != SnapshotType.Delta || _config.AlwaysIncludeGraph;
            w.Write(writeGraph ? (byte)1 : (byte)0);
            if (writeGraph)
            {
                var sortedNodeIds = dimension.Graph.GetNodeIdsSorted();
                w.Write(sortedNodeIds.Count);
                foreach (var id in sortedNodeIds)
                {
                    var node = dimension.Graph.Nodes[id];
                    w.Write(poolIdx[node.Id]);
                    w.Write(poolIdx[node.Name ?? node.Id]);
                }

                var edges = GatherEdgesSorted(dimension.Graph, sortedNodeIds);
                w.Write(edges.Count);
                foreach (var edge in edges)
                {
                    w.Write(poolIdx[edge.FromId]);
                    w.Write(poolIdx[edge.ToId]);
                    w.Write(edge.Resistance);
                    var sortedTags = edge.Tags.OrderBy(t => t, StringComparer.Ordinal).ToList();
                    w.Write(sortedTags.Count);
                    foreach (var tag in sortedTags) w.Write(poolIdx[tag]);
                }
            }

            // ── Fields ────────────────────────────────────────────────────────
            var fieldIds = dimension.Fields.Keys.OrderBy(f => f, StringComparer.Ordinal).ToList();
            w.Write(fieldIds.Count);

            foreach (var fieldId in fieldIds)
            {
                var field   = dimension.Fields[fieldId];
                var profile = field.Profile;

                w.Write(poolIdx[fieldId]);
                w.Write(poolIdx[profile.ProfileId]);
                w.Write(profile.PropagationRate);
                w.Write(profile.EdgeResistanceScale);
                w.Write(profile.DecayRate);
                w.Write(profile.MinLogAmpClamp);
                w.Write(profile.MaxLogAmpClamp);
                w.Write(profile.LogEpsilon);

                var activeEntries = field.EnumerateAllActiveSorted().ToList();

                if (_config.ValidateLogAmpSanityOnWrite)
                    foreach (var (nId, chId, logAmp) in activeEntries)
                        if (float.IsNaN(logAmp) || float.IsInfinity(logAmp))
                            throw new InvalidOperationException(
                                $"NaN/Infinity in field '{fieldId}' ({nId},{chId})={logAmp}");

                if (previousEntries == null)
                {
                    // Full / Checkpoint — write all active entries
                    w.Write(activeEntries.Count);
                    foreach (var (nId, chId, logAmp) in activeEntries)
                    {
                        w.Write(poolIdx[nId]);
                        w.Write(poolIdx[chId]);
                        w.Write(logAmp);
                    }
                }
                else
                {
                    // Delta — only changed entries
                    var currentMap = activeEntries
                        .ToDictionary(e => (e.nodeId, e.channelId), e => e.logAmp);
                    var delta = new List<(string nodeId, string channelId, float logAmp)>();

                    // New or changed
                    foreach (var (key, newAmp) in currentMap)
                    {
                        var fk = (fieldId, key.nodeId, key.channelId);
                        if (!previousEntries.TryGetValue(fk, out float oldAmp) || oldAmp != newAmp)
                            delta.Add((key.nodeId, key.channelId, newAmp));
                    }
                    // Removed (logAmp=0 sentinel means "gone")
                    foreach (var (fk, _) in previousEntries)
                    {
                        if (fk.fieldId != fieldId) continue;
                        if (!currentMap.ContainsKey((fk.nodeId, fk.channelId)))
                            delta.Add((fk.nodeId, fk.channelId, 0f));
                    }
                    // Sort for determinism
                    delta.Sort((a, b) =>
                    {
                        int c = StringComparer.Ordinal.Compare(a.nodeId, b.nodeId);
                        return c != 0 ? c : StringComparer.Ordinal.Compare(a.channelId, b.channelId);
                    });

                    w.Write(delta.Count);
                    foreach (var (nId, chId, logAmp) in delta)
                    {
                        w.Write(poolIdx[nId]);
                        w.Write(poolIdx[chId]);
                        w.Write(logAmp);
                    }
                }
            }

            // ── System blobs ──────────────────────────────────────────────────
            int blobCount = participants?.Count ?? 0;
            w.Write(blobCount);
            if (participants != null)
            {
                // Sorted by SystemId for byte-determinism
                foreach (var p in participants.OrderBy(p => p.SystemId, StringComparer.Ordinal))
                {
                    byte[] payload = p.SerializeSystemState();
                    w.Write(poolIdx[p.SystemId]);
                    w.Write((ushort)1); // outer blob schema version (always 1 here)
                    w.Write(payload.Length);
                    w.Write(payload);
                }
            }

            return ms.ToArray();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string[] BuildPool(Dimension dimension,
            IReadOnlyList<ISnapshotParticipant> participants,
            Dictionary<(string fieldId, string nodeId, string channelId), float> previousEntries = null)
        {
            var set = new HashSet<string>(StringComparer.Ordinal) { "" }; // index-0 sentinel

            foreach (var (_, node) in dimension.Graph.Nodes)
            {
                set.Add(node.Id);
                set.Add(node.Name ?? node.Id);
            }
            foreach (var nodeId in dimension.Graph.GetNodeIdsSorted())
                foreach (var edge in dimension.Graph.GetOutEdgesSorted(nodeId))
                {
                    set.Add(edge.FromId);
                    set.Add(edge.ToId);
                    foreach (var tag in edge.Tags) set.Add(tag);
                }

            foreach (var (fieldId, field) in dimension.Fields)
            {
                set.Add(fieldId);
                set.Add(field.Profile.ProfileId);
                foreach (var (nId, chId, _) in field.EnumerateAllActiveSorted())
                {
                    set.Add(nId);
                    set.Add(chId);
                }
            }

            if (participants != null)
                foreach (var p in participants) set.Add(p.SystemId);

            // Include strings from the previous snapshot so that sentinel-zero entries
            // for removed nodes/channels can be encoded even when those strings no longer
            // appear in any currently active field entry.
            if (previousEntries != null)
                foreach (var (fk, _) in previousEntries)
                {
                    set.Add(fk.fieldId);
                    set.Add(fk.nodeId);
                    set.Add(fk.channelId);
                }

            var sorted = set.ToArray();
            Array.Sort(sorted, StringComparer.Ordinal);
            return sorted;
        }

        private static List<Edge> GatherEdgesSorted(NodeGraph graph, IReadOnlyList<string> sortedNodeIds)
        {
            var list = new List<Edge>();
            foreach (var nodeId in sortedNodeIds)
                list.AddRange(graph.GetOutEdgesSorted(nodeId));
            return list;
        }

        private static Dictionary<(string fieldId, string nodeId, string channelId), float>
            BuildPreviousEntryMap(SnapshotData snap)
        {
            var map = new Dictionary<(string, string, string), float>();
            foreach (var f in snap.Fields)
                foreach (var (nId, chId, logAmp) in f.Entries)
                    map[(f.FieldId, nId, chId)] = logAmp;
            return map;
        }

        private static void WriteStr(BinaryWriter w, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            w.Write(bytes.Length);
            w.Write(bytes);
        }
    }
}
