using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Odengine.Fields;

namespace Odengine.Serialization
{
    /// <summary>
    /// Reads Odengine snapshots written by SnapshotWriter.
    /// Thread-safe for concurrent reads of independent byte arrays.
    /// </summary>
    public sealed class SnapshotReader
    {
        private readonly SnapshotConfig _config;

        public SnapshotReader(SnapshotConfig config = null)
        {
            _config = config ?? SnapshotConfig.Default;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Read any snapshot type (Full, Delta, Checkpoint).</summary>
        public SnapshotData Read(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < 8)
                throw new InvalidSnapshotException("Buffer is too short to be a valid Odengine snapshot.");

            using var ms = new MemoryStream(bytes, writable: false);
            using var r  = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            return ReadFrom(r);
        }

        /// <summary>
        /// Reconstruct field state at <paramref name="targetTick"/> by reading a Full
        /// ancestor and applying subsequent Deltas. The series must be in tick order
        /// (ascending) but does not need to be contiguous.
        /// Graph and system blobs come from the Full/Checkpoint ancestor.
        /// </summary>
        public SnapshotData ReadAtTick(IReadOnlyList<byte[]> snapshotSeries, ulong targetTick)
        {
            if (snapshotSeries == null) throw new ArgumentNullException(nameof(snapshotSeries));
            if (snapshotSeries.Count == 0)
                throw new MissingParentSnapshotException("Snapshot series is empty.");

            var parsed = snapshotSeries.Select(Read).OrderBy(s => s.Header.Tick).ToList();

            // Latest Full/Checkpoint at or before targetTick
            SnapshotData baseSnap = null;
            foreach (var snap in parsed)
            {
                if (snap.Header.Tick > targetTick) break;
                if (snap.Header.SnapshotType != SnapshotType.Delta) baseSnap = snap;
            }
            if (baseSnap == null)
                throw new MissingParentSnapshotException(
                    $"No Full/Checkpoint snapshot at or before tick {targetTick} in the series.");

            var deltas = parsed
                .Where(s => s.Header.SnapshotType == SnapshotType.Delta
                         && s.Header.Tick > baseSnap.Header.Tick
                         && s.Header.Tick <= targetTick)
                .OrderBy(s => s.Header.Tick)
                .ToList();

            return deltas.Count == 0 ? baseSnap : MergeDeltas(baseSnap, deltas);
        }

        // ── Private deserialization ───────────────────────────────────────────

        private SnapshotData ReadFrom(BinaryReader r)
        {
            // ── Header ────────────────────────────────────────────────────────
            uint magic = r.ReadUInt32();
            if (magic != SnapshotWriter.Magic)
                throw new InvalidSnapshotException(
                    $"Invalid magic 0x{magic:X8}. Expected 0x{SnapshotWriter.Magic:X8} ('ODSN').");

            ushort schemaVersion = r.ReadUInt16();
            if (schemaVersion > SnapshotWriter.SchemaVersion)
                throw new SnapshotVersionException(
                    $"Snapshot schema version {schemaVersion} is newer than this build supports " +
                    $"(max {SnapshotWriter.SchemaVersion}). Upgrade Odengine to read this file.");

            var snapType      = (SnapshotType)r.ReadByte();
            ulong tick        = r.ReadUInt64();
            double simTime    = r.ReadDouble();
            ulong createdUtc  = r.ReadUInt64();
            ulong parentTick  = r.ReadUInt64();
            ushort chainDepth = r.ReadUInt16();
            string engineVer  = ReadStr(r);

            var header = new SnapshotHeader(schemaVersion, snapType, tick, simTime,
                createdUtc, parentTick, chainDepth, engineVer);

            // ── String pool ───────────────────────────────────────────────────
            int poolCount = r.ReadInt32();
            var pool = new string[poolCount];
            for (int i = 0; i < poolCount; i++) pool[i] = ReadStr(r);

            // ── Graph ─────────────────────────────────────────────────────────
            bool hasGraph = r.ReadByte() != 0;
            var nodes = Array.Empty<GraphNodeRecord>();
            var edges = Array.Empty<GraphEdgeRecord>();
            if (hasGraph)
            {
                int nodeCount = r.ReadInt32();
                nodes = new GraphNodeRecord[nodeCount];
                for (int i = 0; i < nodeCount; i++)
                    nodes[i] = new GraphNodeRecord { Id = pool[r.ReadUInt32()], Name = pool[r.ReadUInt32()] };

                int edgeCount = r.ReadInt32();
                edges = new GraphEdgeRecord[edgeCount];
                for (int i = 0; i < edgeCount; i++)
                {
                    string from = pool[r.ReadUInt32()];
                    string to   = pool[r.ReadUInt32()];
                    float  res  = r.ReadSingle();
                    int tagCount = r.ReadInt32();
                    var tags = new string[tagCount];
                    for (int t = 0; t < tagCount; t++) tags[t] = pool[r.ReadUInt32()];
                    edges[i] = new GraphEdgeRecord { FromId = from, ToId = to, Resistance = res, Tags = tags };
                }
            }

            // ── Fields ────────────────────────────────────────────────────────
            int fieldCount = r.ReadInt32();
            var fields = new FieldRecord[fieldCount];
            for (int f = 0; f < fieldCount; f++)
            {
                string fieldId   = pool[r.ReadUInt32()];
                string profileId = pool[r.ReadUInt32()];
                var profile = new FieldProfile(profileId)
                {
                    PropagationRate     = r.ReadSingle(),
                    EdgeResistanceScale = r.ReadSingle(),
                    DecayRate           = r.ReadSingle(),
                    MinLogAmpClamp      = r.ReadSingle(),
                    MaxLogAmpClamp      = r.ReadSingle(),
                    LogEpsilon          = r.ReadSingle()
                };

                int entryCount = r.ReadInt32();
                var entries = new (string nodeId, string channelId, float logAmp)[entryCount];
                for (int e = 0; e < entryCount; e++)
                    entries[e] = (pool[r.ReadUInt32()], pool[r.ReadUInt32()], r.ReadSingle());

                fields[f] = new FieldRecord { FieldId = fieldId, Profile = profile, Entries = entries };
            }

            // ── System blobs ──────────────────────────────────────────────────
            int blobCount = r.ReadInt32();
            var blobs = new SystemBlobRecord[blobCount];
            for (int b = 0; b < blobCount; b++)
            {
                string systemId   = pool[r.ReadUInt32()];
                ushort blobVer    = r.ReadUInt16();
                int    payloadLen = r.ReadInt32();
                byte[] payload    = r.ReadBytes(payloadLen);
                blobs[b] = new SystemBlobRecord { SystemId = systemId, SchemaVersion = blobVer, Payload = payload };
            }

            return new SnapshotData(header, nodes, edges, fields, blobs);
        }

        // ── Delta merge ───────────────────────────────────────────────────────

        private static SnapshotData MergeDeltas(SnapshotData baseSnap,
            IReadOnlyList<SnapshotData> deltas)
        {
            // Build mutable entry maps indexed by fieldId
            var fieldMaps    = new Dictionary<string, Dictionary<(string, string), float>>(StringComparer.Ordinal);
            var fieldProfiles = new Dictionary<string, FieldProfile>(StringComparer.Ordinal);

            foreach (var f in baseSnap.Fields)
            {
                fieldProfiles[f.FieldId] = f.Profile;
                var map = new Dictionary<(string, string), float>();
                foreach (var (nId, chId, amp) in f.Entries) map[(nId, chId)] = amp;
                fieldMaps[f.FieldId] = map;
            }

            // Apply each delta in ascending tick order
            foreach (var delta in deltas)
            {
                foreach (var f in delta.Fields)
                {
                    if (!fieldMaps.TryGetValue(f.FieldId, out var map))
                    {
                        map = new Dictionary<(string, string), float>();
                        fieldMaps[f.FieldId] = map;
                        fieldProfiles[f.FieldId] = f.Profile;
                    }
                    foreach (var (nId, chId, amp) in f.Entries)
                    {
                        if (amp == 0f) map.Remove((nId, chId));
                        else           map[(nId, chId)] = amp;
                    }
                }
            }

            // Reconstruct sorted FieldRecord[]
            var sortedFieldIds = fieldMaps.Keys.OrderBy(f => f, StringComparer.Ordinal).ToList();
            var mergedFields = new FieldRecord[sortedFieldIds.Count];
            for (int i = 0; i < sortedFieldIds.Count; i++)
            {
                var fid  = sortedFieldIds[i];
                var map  = fieldMaps[fid];
                var entries = map
                    .OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal)
                    .ThenBy(kv  => kv.Key.Item2, StringComparer.Ordinal)
                    .Select(kv  => (kv.Key.Item1, kv.Key.Item2, kv.Value))
                    .ToArray();
                mergedFields[i] = new FieldRecord { FieldId = fid, Profile = fieldProfiles[fid], Entries = entries };
            }

            var last = deltas[deltas.Count - 1];
            var mergedHeader = new SnapshotHeader(
                last.Header.SchemaVersion,
                baseSnap.Header.SnapshotType,  // preserve Checkpoint type from base
                last.Header.Tick,
                last.Header.SimTime,
                last.Header.CreatedUtcMs,
                parentTick: 0,
                deltaChainDepth: 0,
                last.Header.EngineVersion);

            return new SnapshotData(mergedHeader,
                baseSnap.Nodes, baseSnap.Edges,
                mergedFields, baseSnap.SystemBlobs);
        }

        private static string ReadStr(BinaryReader r)
        {
            int len = r.ReadInt32();
            return len == 0 ? string.Empty : Encoding.UTF8.GetString(r.ReadBytes(len));
        }
    }
}
