using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Odengine.Core;

namespace Odengine.Tests.Shared
{
    /// <summary>
    /// Deterministic content hash for a Dimension.
    /// Used in determinism and replay tests.
    /// Sort order: nodes alphabetical, edges by from→to, fields by fieldId,
    /// amplitudes by channelId then nodeId — all StringComparer.Ordinal.
    /// </summary>
    public static class StateHash
    {
        public static string Compute(Dimension dim)
        {
            var sb = new StringBuilder();

            // Nodes
            foreach (var nodeId in dim.Graph.GetNodeIdsSorted())
                sb.Append("N:").Append(nodeId).Append('\n');

            // Edges (stable because GetOutEdgesSorted is already sorted)
            foreach (var fromId in dim.Graph.GetNodeIdsSorted())
            {
                foreach (var e in dim.Graph.GetOutEdgesSorted(fromId))
                {
                    var tags = string.Join(",", e.Tags.OrderBy(t => t, StringComparer.Ordinal));
                    sb.Append("E:")
                      .Append(e.FromId).Append("->").Append(e.ToId)
                      .Append("|r=").Append(e.Resistance.ToString("R"))
                      .Append("|tags=").Append(tags)
                      .Append('\n');
                }
            }

            // Fields (sorted by fieldId)
            foreach (var fieldId in dim.Fields.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var field = dim.Fields[fieldId];
                sb.Append("F:").Append(fieldId).Append('\n');

                // EnumerateAllActiveSorted already sorts by channelId then nodeId
                foreach (var (nodeId, channelId, logAmp) in field.EnumerateAllActiveSorted())
                {
                    // Bitwise float identity — avoids rounding in ToString
                    int bits = BitConverter.SingleToInt32Bits(logAmp);
                    sb.Append("A:")
                      .Append(channelId).Append('|').Append(nodeId)
                      .Append('|').Append(bits)
                      .Append('\n');
                }
            }

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
    }
}
