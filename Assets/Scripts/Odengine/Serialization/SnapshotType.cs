namespace Odengine.Serialization
{
    public enum SnapshotType : byte
    {
        /// <summary>Complete state. Self-contained. No parent required for reconstruction.</summary>
        Full = 0,

        /// <summary>
        /// Only entries that changed since the parent snapshot.
        /// Entries with logAmp == 0 mean "removed since parent".
        /// Requires a Full or Checkpoint ancestor to reconstruct.
        /// </summary>
        Delta = 1,

        /// <summary>
        /// Full state + all system blobs from registered ISnapshotParticipants.
        /// The only snapshot type that supports simulation resume (load-and-continue).
        /// </summary>
        Checkpoint = 2
    }
}
