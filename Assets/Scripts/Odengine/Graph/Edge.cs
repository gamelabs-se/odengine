using System;
using System.Collections.Generic;
using System.Linq;

namespace Odengine.Graph
{
    /// <summary>
    /// An edge between two nodes.
    /// The ONLY way fields move between nodes.
    /// Has exactly one core property: Resistance.
    /// </summary>
    public sealed class OdEdge
    {
        public OdNode From { get; }
        public OdNode To { get; }
        public float Resistance { get; set; }
        
        private readonly SortedSet<string> _tags;

        public OdEdge(OdNode from, OdNode to, float resistance = 1f)
        {
            From = from ?? throw new ArgumentNullException(nameof(from));
            To = to ?? throw new ArgumentNullException(nameof(to));
            Resistance = resistance;
            _tags = new SortedSet<string>(StringComparer.Ordinal);
        }

        public void AddTag(string tag) => _tags.Add(tag);
        public bool HasTag(string tag) => _tags.Contains(tag);
        public IReadOnlyCollection<string> Tags => _tags;

        public override string ToString() => $"Edge({From.Id} -> {To.Id}, R={Resistance:F2})";
    }
}
