using System;

namespace Odengine.Serialization
{
    /// <summary>Thrown when the file is not a valid Odengine snapshot (wrong magic, truncated, corrupt).</summary>
    public sealed class InvalidSnapshotException : Exception
    {
        public InvalidSnapshotException(string message) : base(message) { }
        public InvalidSnapshotException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Thrown when the snapshot's schema version is too new for this build to read.</summary>
    public sealed class SnapshotVersionException : Exception
    {
        public SnapshotVersionException(string message) : base(message) { }
    }

    /// <summary>Thrown when ReadAtTick cannot find a Full/Checkpoint ancestor in the series.</summary>
    public sealed class MissingParentSnapshotException : Exception
    {
        public MissingParentSnapshotException(string message) : base(message) { }
    }
}
