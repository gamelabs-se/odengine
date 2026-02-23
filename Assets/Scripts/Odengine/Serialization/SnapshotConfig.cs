namespace Odengine.Serialization
{
    public sealed class SnapshotConfig
    {
        /// <summary>
        /// After this many consecutive Delta snapshots the writer will recommend
        /// emitting a new Full to bound replay-seek cost. 0 = no limit.
        /// (Advisory only — caller decides when to emit Full vs Delta.)
        /// </summary>
        public int DeltaChainMaxLength = 100;

        /// <summary>
        /// When true, verify that all field-entry nodeIds exist in the Dimension graph before
        /// writing. Catches logic errors at the cost of a dictionary lookup per entry.
        /// Recommended: true in debug/test builds.
        /// </summary>
        public bool ValidateGraphReferencesOnWrite = false;

        /// <summary>
        /// When true, refuse to write NaN or Infinity logAmp values.
        /// Should never trigger if ScalarField clamping is functioning correctly.
        /// Recommended: true in debug builds.
        /// </summary>
        public bool ValidateLogAmpSanityOnWrite = false;

        /// <summary>
        /// When true, check that field-entry nodeIds in a loaded snapshot exist in the
        /// restored graph. Entries for missing nodes are skipped with a warning.
        /// </summary>
        public bool ValidateGraphReferencesOnRead = true;

        /// <summary>
        /// When true, write the full graph (nodes + edges) even in Delta snapshots.
        /// Default false: graph is assumed stable between a Full and its Deltas.
        /// Enable if the graph can mutate mid-run.
        /// </summary>
        public bool AlwaysIncludeGraph = false;

        /// <summary>Engine version baked into every snapshot header.</summary>
        public string EngineVersion = "0.1.0";

        public static readonly SnapshotConfig Default = new SnapshotConfig();
    }
}
