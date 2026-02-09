using System;
using System.Collections.Generic;
using System.Linq;

namespace Odengine.Graph
{
    /// <summary>
    /// A node in the world graph.
    /// Nodes are terrain. Fields flow over them.
    /// </summary>
    public sealed class OdNode
    {
        public string Id { get; }
        public string Name { get; set; }
        
        private readonly SortedSet<string> _tags;
        private readonly Dictionary<string, object> _components;
        private readonly List<OdEdge> _edges;
        private List<OdEdge> _sortedEdges;
        private bool _needsEdgeSort;

        public OdNode(string id, string name = null)
        {
            Id = id;
            Name = name ?? id;
            _tags = new SortedSet<string>(StringComparer.Ordinal);
            _components = new Dictionary<string, object>(StringComparer.Ordinal);
            _edges = new List<OdEdge>();
            _sortedEdges = new List<OdEdge>();
            _needsEdgeSort = false;
        }

        public void AddTag(string tag)
        {
            _tags.Add(tag);
        }

        public bool HasTag(string tag) => _tags.Contains(tag);
        public IReadOnlyCollection<string> Tags => _tags;

        public void SetComponent<T>(string key, T component) => _components[key] = component;
        
        public bool TryGetComponent<T>(string key, out T component)
        {
            if (_components.TryGetValue(key, out var obj) && obj is T typed)
            {
                component = typed;
                return true;
            }
            component = default;
            return false;
        }

        internal void AddEdge(OdEdge edge)
        {
            _edges.Add(edge);
            _needsEdgeSort = true;
        }

        /// <summary>
        /// Get edges in deterministic order (sorted by target node ID).
        /// CRITICAL for determinism.
        /// </summary>
        public IReadOnlyList<OdEdge> GetSortedEdges()
        {
            if (_needsEdgeSort)
            {
                _sortedEdges = _edges.OrderBy(e => e.To.Id, StringComparer.Ordinal).ToList();
                _needsEdgeSort = false;
            }
            return _sortedEdges;
        }

        public override string ToString() => $"Node({Id})";
    }
}
