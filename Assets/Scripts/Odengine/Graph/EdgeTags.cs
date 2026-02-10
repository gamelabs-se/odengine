using System;

namespace Odengine.Graph
{
    [Flags]
    public enum EdgeTags : uint
    {
        None = 0,
        Ocean = 1 << 0,
        Road = 1 << 1,
        Border = 1 << 2,
        Wormhole = 1 << 3,
        Asteroid = 1 << 4,
        // Add more as needed
    }
}
