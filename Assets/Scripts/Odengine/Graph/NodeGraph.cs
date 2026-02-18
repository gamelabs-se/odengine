using System;
using System.Collections.Generic;
using System.Linq;

namespace Odengine.Graph
{
    [Serializable]
    public sealed class NodeGraph
    {
        private readonly Dictionary<string, Node> _nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Edge>> _outEdges = new Dictionary<string, List<Edge>>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, Node> Nodes => _nodes;

        public void AddNode(Node node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            _nodes[node.Id] = node;
            if (!_outEdges.ContainsKey(node.Id))
                _outEdges[node.Id] = new List<Edge>();
        }

        public void AddOrUpdateNode(Node node)
        {
            AddNode(node);
        }

        public bool TryGetNode(string id, out Node node) => _nodes.TryGetValue(id, out node);

        public void AddEdge(string fromId, string toId, float resistance, params string[] tags)
        {
            if (!_nodes.ContainsKey(fromId))
                throw new InvalidOperationException($"Source node '{fromId}' does not exist");
            if (!_nodes.ContainsKey(toId))
                throw new InvalidOperationException($"Target node '{toId}' does not exist");

            var edge = new Edge(fromId, toId, resistance, tags);
            
            if (!_outEdges.TryGetValue(fromId, out var edges))
            {
                edges = new List<Edge>();
                _outEdges[fromId] = edges;
            }

            edges.Add(edge);
            
            // Keep sorted for determinism
            edges.Sort((a, b) =>
            {
                int cmp = StringComparer.Ordinal.Compare(a.ToId, b.ToId);
                if (cmp != 0) return cmp;
                return a.Resistance.CompareTo(b.Resistance);
            });
        }

        public IReadOnlyList<Edge> GetOutEdgesSorted(string fromId)
        {
            if (_outEdges.TryGetValue(fromId, out var edges))
                return edges;
            return Array.Empty<Edge>();
        }

        public IReadOnlyList<string> GetNodeIdsSorted()
        {
            var ids = _nodes.Keys.ToList();
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }
    }
}
