namespace Odengine.Serialization
{
    public readonly struct SnapshotHeader
    {
        public readonly ushort SchemaVersion;
        public readonly SnapshotType SnapshotType;
        public readonly ulong Tick;
        public readonly double SimTime;
        public readonly ulong CreatedUtcMs;
        public readonly ulong ParentTick;
        public readonly ushort DeltaChainDepth;
        public readonly string EngineVersion;

        internal SnapshotHeader(
            ushort schemaVersion, SnapshotType snapshotType,
            ulong tick, double simTime, ulong createdUtcMs,
            ulong parentTick, ushort deltaChainDepth, string engineVersion)
        {
            SchemaVersion = schemaVersion;
            SnapshotType = snapshotType;
            Tick = tick;
            SimTime = simTime;
            CreatedUtcMs = createdUtcMs;
            ParentTick = parentTick;
            DeltaChainDepth = deltaChainDepth;
            EngineVersion = engineVersion;
        }
    }
}
