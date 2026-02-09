using System;
using System.Collections.Generic;
using System.Linq;

namespace Odengine.Graph
{
    /// <summary>
    /// Graph wrapper for managing nodes and their relationships.
    /// DETERMINISTIC: All iteration is ordered (StringComparer.Ordinal).
    /// </summary>
    public sealed class OdNodeGraph
    {
        private readonly Dictionary<string, OdNode> _nodes;
        private readonly List<OdEdge> _edges;
        private List<string> _sortedNodeIds;
        private bool _needsSort;

        public int NodeCount => _nodes.Count;
        public int EdgeCount => _edges.Count;

        public OdNodeGraph()
        {
            _nodes = new Dictionary<string, OdNode>(StringComparer.Ordinal);
            _edges = new List<OdEdge>();
            _sortedNodeIds = new List<string>();
            _needsSort = false;
        }

        public void AddNode(OdNode node)
        {
            if (_nodes.ContainsKey(node.Id))
                throw new InvalidOperationException($"Node {node.Id} already exists");
            
            _nodes[node.Id] = node;
            _needsSort = true;
        }

        public bool TryGetNode(string id, out OdNode node)
        {
            return _nodes.TryGetValue(id, out node);
        }

        public OdNode GetNode(string id)
        {
            return _nodes.TryGetValue(id, out var node) ? node : null;
        }

        /// <summary>
        /// Add an edge and return it. Can optionally add reverse edge for undirected behavior.
        /// </summary>
        public OdEdge AddEdge(string fromId, string toId, float resistance = 1.0f, bool bidirectional = false)
        {
            if (!_nodes.TryGetValue(fromId, out var from))
                throw new ArgumentException($"Source node {fromId} not found");
            if (!_nodes.TryGetValue(toId, out var to))
                throw new ArgumentException($"Target node {toId} not found");

            var edge = new OdEdge(from, to, resistance);
            from.AddEdge(edge);
            _edges.Add(edge);

            if (bidirectional)
            {
                var reverseEdge = new OdEdge(to, from, resistance);
                to.AddEdge(reverseEdge);
                _edges.Add(reverseEdge);
            }

            return edge;
        }

        /// <summary>
        /// Get node IDs in stable sorted order. CRITICAL for determinism.
        /// </summary>
        public IReadOnlyList<string> GetSortedNodeIds()
        {
            if (_needsSort)
            {
                _sortedNodeIds = _nodes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                _needsSort = false;
            }
            return _sortedNodeIds;
        }

        /// <summary>
        /// Iterate nodes in deterministic order.
        /// </summary>
        public IEnumerable<OdNode> EnumerateNodesSorted()
        {
            foreach (var id in GetSortedNodeIds())
                yield return _nodes[id];
        }

        public IEnumerable<OdEdge> AllEdges => _edges;
        public IReadOnlyDictionary<string, OdNode> Nodes => _nodes;
    }
}
