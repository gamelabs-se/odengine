using System;
using System.Collections.Generic;
using System.Linq;

namespace Odengine.Graph
{
    [Serializable]
    public sealed class Edge
    {
        public string FromId { get; }
        public string ToId { get; }
        public float Resistance { get; }
        public HashSet<string> Tags { get; }

        public Edge(string fromId, string toId, float resistance, params string[] tags)
        {
            if (string.IsNullOrEmpty(fromId))
                throw new ArgumentException("FromId cannot be null or empty", nameof(fromId));
            if (string.IsNullOrEmpty(toId))
                throw new ArgumentException("ToId cannot be null or empty", nameof(toId));
            if (resistance < 0f)
                throw new ArgumentException("Resistance must be >= 0", nameof(resistance));

            FromId = fromId;
            ToId = toId;
            Resistance = resistance;
            Tags = tags != null && tags.Length > 0 
                ? new HashSet<string>(tags, StringComparer.Ordinal) 
                : new HashSet<string>(StringComparer.Ordinal);
        }

        public bool HasTag(string tag) => Tags.Contains(tag);

        public override string ToString()
        {
            var tagStr = Tags.Count > 0 ? $" [{string.Join(",", Tags.OrderBy(t => t))}]" : "";
            return $"Edge({FromId}->{ToId}, R={Resistance:F2}{tagStr})";
        }
    }
}
