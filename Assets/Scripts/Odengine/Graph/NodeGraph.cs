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
        /// Add an edge and return it. Tags can be comma-separated string or EdgeTags flags.
        /// </summary>
        public OdEdge AddEdge(string fromId, string toId, float resistance = 1.0f, string tags = "")
        {
            if (!_nodes.TryGetValue(fromId, out var from))
                throw new ArgumentException($"Source node {fromId} not found");
            if (!_nodes.TryGetValue(toId, out var to))
                throw new ArgumentException($"Target node {toId} not found");

            var edge = new OdEdge(from, to, resistance);
            
            // Apply tags from string
            if (!string.IsNullOrEmpty(tags))
            {
                var tagList = tags.Split(',');
                foreach (var tag in tagList)
                {
                    var trimmed = tag.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        edge.AddTag(trimmed);
                }
            }
            
            from.AddEdge(edge);
            _edges.Add(edge);
            return edge;
        }

        /// <summary>
        /// Add bidirectional edges (convenience method)
        /// </summary>
        public void AddBidirectionalEdge(string fromId, string toId, float resistance = 1.0f, string tags = "")
        {
            AddEdge(fromId, toId, resistance, tags);
            AddEdge(toId, fromId, resistance, tags);
        }
        
        /// <summary>
        /// Add edge with EdgeTags enum (convenience)
        /// </summary>
        public OdEdge AddEdgeWithTags(string fromId, string toId, float resistance, EdgeTags tagFlags)
        {
            if (!_nodes.TryGetValue(fromId, out var from))
                throw new ArgumentException($"Source node {fromId} not found");
            if (!_nodes.TryGetValue(toId, out var to))
                throw new ArgumentException($"Target node {toId} not found");

            var edge = new OdEdge(from, to, resistance);
            
            // Apply tags from enum flags
            if (tagFlags != EdgeTags.None)
            {
                if ((tagFlags & EdgeTags.Ocean) != 0) edge.AddTag("ocean");
                if ((tagFlags & EdgeTags.Road) != 0) edge.AddTag("road");
                if ((tagFlags & EdgeTags.Wormhole) != 0) edge.AddTag("wormhole");
                if ((tagFlags & EdgeTags.Border) != 0) edge.AddTag("border");
            }
            
            from.AddEdge(edge);
            _edges.Add(edge);
            return edge;
        }

        [Obsolete("Use AddEdge or AddEdgeWithTags instead")]
        private OdEdge AddEdge_OLD(string fromId, string toId, float resistance = 1.0f, EdgeTags tags = EdgeTags.None, bool bidirectional = false)
        {
            if (!_nodes.TryGetValue(fromId, out var from))
                throw new ArgumentException($"Source node {fromId} not found");
            if (!_nodes.TryGetValue(toId, out var to))
                throw new ArgumentException($"Target node {toId} not found");

            var edge = new OdEdge(from, to, resistance);
            
            // Apply tags
            if (tags != EdgeTags.None)
            {
                if ((tags & EdgeTags.Ocean) != 0) edge.AddTag("ocean");
                if ((tags & EdgeTags.Road) != 0) edge.AddTag("road");
                if ((tags & EdgeTags.Wormhole) != 0) edge.AddTag("wormhole");
                if ((tags & EdgeTags.Border) != 0) edge.AddTag("border");
            }
            
            from.AddEdge(edge);
            _edges.Add(edge);

            if (bidirectional)
            {
                var reverseEdge = new OdEdge(to, from, resistance);
                
                // Copy tags to reverse edge
                if (tagFlags != EdgeTags.None)
                {
                    if ((tagFlags & EdgeTags.Ocean) != 0) reverseEdge.AddTag("ocean");
                    if ((tagFlags & EdgeTags.Road) != 0) reverseEdge.AddTag("road");
                    if ((tagFlags & EdgeTags.Wormhole) != 0) reverseEdge.AddTag("wormhole");
                    if ((tagFlags & EdgeTags.Border) != 0) reverseEdge.AddTag("border");
                }
                
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
