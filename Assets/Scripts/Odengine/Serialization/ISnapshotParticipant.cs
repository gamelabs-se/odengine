namespace Odengine.Serialization
{
    /// <summary>
    /// Implement on any domain system that holds non-field state which must survive
    /// save/load for a simulation to resume correctly.
    ///
    /// When NOT to implement this:
    ///   - State is entirely in ScalarFields (EconomySystem) → serialized automatically.
    ///   - State is a reconstructable cache (FactionSystem._lastDominant) → implement PostLoad() only.
    ///
    /// When to implement:
    ///   - Private HashSets, Dictionaries, progress values that cannot be re-derived from fields.
    ///   - Tuning constants that must match the original run exactly (see WarConfig).
    ///
    /// Invariants:
    ///   - SerializeSystemState() must be deterministic: same state → identical bytes.
    ///   - Start the payload with a version byte. Start at 1. Increment when layout changes.
    ///   - SystemId must never change — it is the primary key for the blob in saved files.
    /// </summary>
    public interface ISnapshotParticipant
    {
        /// <summary>
        /// Stable unique identifier for this system's blob.
        /// Convention: "domain.systemname" e.g. "war.system".
        /// </summary>
        string SystemId { get; }

        /// <summary>
        /// Serialize all non-field system state to a deterministic byte array.
        /// First byte(s) must encode the layout version.
        /// All collections must be iterated in Ordinal-sorted order.
        /// </summary>
        byte[] SerializeSystemState();

        /// <summary>
        /// Restore non-field state from a blob produced by SerializeSystemState().
        /// Called after field restoration. The Dimension and all registered fields
        /// are in their saved state when this is invoked.
        /// </summary>
        /// <param name="payload">Raw bytes from the system blob.</param>
        /// <param name="blobSchemaVersion">Blob schema version from the file header.
        /// Switch on this to apply migrations from older formats.</param>
        void DeserializeSystemState(byte[] payload, int blobSchemaVersion);

        /// <summary>
        /// Called after all fields and blobs have been restored.
        /// Override to rebuild derived caches that can be recomputed from field state.
        /// Default: no-op.
        /// </summary>
        void PostLoad() { }
    }
}
