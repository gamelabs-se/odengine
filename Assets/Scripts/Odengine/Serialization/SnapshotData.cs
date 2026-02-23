using System;
using System.Collections.Generic;
using System.Linq;
using Odengine.Core;
using Odengine.Fields;

namespace Odengine.Serialization
{
    // ── Internal data records ──────────────────────────────────────────────────

    internal struct GraphNodeRecord
    {
        public string Id;
        public string Name;
    }

    internal struct GraphEdgeRecord
    {
        public string   FromId;
        public string   ToId;
        public float    Resistance;
        public string[] Tags;
    }

    internal struct FieldRecord
    {
        public string  FieldId;
        public FieldProfile Profile;
        /// <summary>
        /// For Full/Checkpoint: all active entries.
        /// For Delta: changed entries only — logAmp==0 means "entry removed since parent".
        /// After MergeDeltas, always treated as full entries.
        /// </summary>
        public (string nodeId, string channelId, float logAmp)[] Entries;
    }

    internal struct SystemBlobRecord
    {
        public string SystemId;
        public ushort SchemaVersion;
        public byte[] Payload;
    }

    // ── Public handle ──────────────────────────────────────────────────────────

    /// <summary>
    /// The result of reading a snapshot. Provides APIs for reconstructing a Dimension
    /// and restoring system state.
    /// </summary>
    public sealed class SnapshotData
    {
        public SnapshotHeader Header { get; }

        internal GraphNodeRecord[]  Nodes       { get; }
        internal GraphEdgeRecord[]  Edges       { get; }
        internal FieldRecord[]      Fields      { get; }
        internal SystemBlobRecord[] SystemBlobs { get; }

        internal SnapshotData(
            SnapshotHeader header,
            GraphNodeRecord[]  nodes,
            GraphEdgeRecord[]  edges,
            FieldRecord[]      fields,
            SystemBlobRecord[] systemBlobs)
        {
            Header      = header;
            Nodes       = nodes       ?? Array.Empty<GraphNodeRecord>();
            Edges       = edges       ?? Array.Empty<GraphEdgeRecord>();
            Fields      = fields      ?? Array.Empty<FieldRecord>();
            SystemBlobs = systemBlobs ?? Array.Empty<SystemBlobRecord>();
        }

        // ── Reconstruction API ─────────────────────────────────────────────

        /// <summary>
        /// Reconstruct a standalone Dimension for analysis. No domain systems are
        /// constructed — all ScalarField operations work normally.
        /// </summary>
        public Dimension ReconstructDimension()
        {
            var dim = new Dimension();
            RestoreGraph(dim);
            RestoreFields(dim);
            return dim;
        }

        /// <summary>Add saved nodes and edges to an empty Dimension.</summary>
        public void RestoreGraph(Dimension dim)
        {
            if (dim == null) throw new ArgumentNullException(nameof(dim));
            foreach (var n in Nodes)
                dim.AddNode(n.Id, n.Name);
            foreach (var e in Edges)
                dim.AddEdge(e.FromId, e.ToId, e.Resistance, e.Tags);
        }

        /// <summary>
        /// Restore all field registrations and sparse entry data.
        /// Uses GetOrCreateField — safe to call before or after system constructors.
        /// Fields that already exist are written to (not replaced); their saved profile wins.
        /// </summary>
        public void RestoreFields(Dimension dim)
        {
            if (dim == null) throw new ArgumentNullException(nameof(dim));
            foreach (var f in Fields)
            {
                var field = dim.GetOrCreateField(f.FieldId, f.Profile);
                foreach (var (nodeId, channelId, logAmp) in f.Entries)
                    field.SetLogAmp(nodeId, channelId, logAmp); // logAmp=0 prunes entry — correct
            }
        }

        /// <summary>
        /// Restore a participant's non-field state from its SystemBlob.
        /// Calls DeserializeSystemState then PostLoad.
        /// Requires snapshot type == Checkpoint. Throws if no matching blob found.
        /// </summary>
        public void RestoreSystem(ISnapshotParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (Header.SnapshotType != SnapshotType.Checkpoint)
                throw new InvalidOperationException(
                    $"Cannot restore system state from a {Header.SnapshotType} snapshot — " +
                    "Checkpoint required. Use WriteCheckpoint() when saving for resume.");

            if (!TryRestoreSystem(participant))
                throw new InvalidOperationException(
                    $"No system blob found for SystemId '{participant.SystemId}' in this snapshot.");
        }

        /// <summary>
        /// Try to restore a participant from its SystemBlob. Returns false if not found.
        /// Requires snapshot type == Checkpoint.
        /// </summary>
        public bool TryRestoreSystem(ISnapshotParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            foreach (var blob in SystemBlobs)
            {
                if (string.Equals(blob.SystemId, participant.SystemId, StringComparison.Ordinal))
                {
                    participant.DeserializeSystemState(blob.Payload, blob.SchemaVersion);
                    participant.PostLoad();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Enumerate all field names in this snapshot, sorted Ordinally.</summary>
        public IReadOnlyList<string> GetFieldIds() =>
            Fields.Select(f => f.FieldId).OrderBy(f => f, StringComparer.Ordinal).ToList();
    }
}
