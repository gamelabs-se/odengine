using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Odengine.Serialization
{
    /// <summary>
    /// In-memory index of a snapshot series stored in a single stream (or file).
    /// Maps ticks to byte offsets so any point in a run can be seeked in O(log N).
    ///
    /// Typical use — run recording:
    ///   var index = new DeltaIndex();
    ///   var stream = File.Create("run.odsn");
    ///   // each tick:
    ///   long offset = stream.Position;
    ///   byte[] snap = writer.WriteDelta(...);
    ///   stream.Write(snap, 0, snap.Length);
    ///   index.Append(tick, SnapshotType.Delta, offset, snap.Length);
    ///   // at run end:
    ///   index.SaveIndex(indexStream);
    /// </summary>
    public sealed class DeltaIndex
    {
        private readonly List<Entry> _entries = new List<Entry>();

        private struct Entry
        {
            public ulong Tick;
            public SnapshotType Type;
            public long ByteOffset;
            public int ByteLength;
        }

        public int Count => _entries.Count;

        /// <summary>Record a snapshot in the index. Entries should be added in ascending tick order.</summary>
        public void Append(ulong tick, SnapshotType type, long byteOffset, int byteLength)
        {
            _entries.Add(new Entry { Tick = tick, Type = type, ByteOffset = byteOffset, ByteLength = byteLength });
        }

        /// <summary>
        /// Returns the byte range of the most recent Full or Checkpoint at or before
        /// <paramref name="tick"/>. Returns null if none found.
        /// </summary>
        public (long offset, int length)? FindFullBefore(ulong tick)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Tick > tick) continue;
                if (e.Type != SnapshotType.Delta) return (e.ByteOffset, e.ByteLength);
            }
            return null;
        }

        /// <summary>
        /// Returns byte ranges of all Deltas with tick in (<paramref name="afterTick"/>,
        /// <paramref name="upToTick"/>], in ascending order.
        /// </summary>
        public IReadOnlyList<(long offset, int length)> FindDeltaRange(ulong afterTick, ulong upToTick)
        {
            var result = new List<(long, int)>();
            foreach (var e in _entries)
            {
                if (e.Tick <= afterTick || e.Tick > upToTick) continue;
                if (e.Type == SnapshotType.Delta) result.Add((e.ByteOffset, e.ByteLength));
            }
            return result;
        }

        /// <summary>Returns the tick of the most recently appended entry, or 0 if empty.</summary>
        public ulong LastTick => _entries.Count > 0 ? _entries[_entries.Count - 1].Tick : 0UL;

        // ── Persistence ───────────────────────────────────────────────────────

        public void SaveIndex(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            w.Write(_entries.Count);
            foreach (var e in _entries)
            {
                w.Write(e.Tick);
                w.Write((byte)e.Type);
                w.Write(e.ByteOffset);
                w.Write(e.ByteLength);
            }
        }

        public static DeltaIndex LoadIndex(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var idx = new DeltaIndex();
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
                idx._entries.Add(new Entry
                {
                    Tick = r.ReadUInt64(),
                    Type = (SnapshotType)r.ReadByte(),
                    ByteOffset = r.ReadInt64(),
                    ByteLength = r.ReadInt32()
                });
            return idx;
        }
    }
}
