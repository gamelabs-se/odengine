using System;
using System.Collections.Generic;
using Odengine.Fields;
using Odengine.Graph;

namespace Odengine.Core
{
    /// <summary>
    /// The world state: just nodes and fields.
    /// No hard-coded game concepts.
    /// Everything is amplitude over a graph.
    /// </summary>
    public sealed class Dimension
    {
        private readonly Dictionary<string, Field> _fields;
        public readonly NodeGraph Graph;

        public IReadOnlyDictionary<string, Field> Fields => _fields;

        public Dimension()
        {
            Graph = new NodeGraph();
            _fields = new Dictionary<string, Field>(StringComparer.Ordinal);
        }

        public Node AddNode(string id, string name = null)
        {
            var node = new Node(id);
            Graph.AddNode(node);
            return node;
        }

        public Edge AddEdge(string fromId, string toId, float resistance = 1.0f, string tags = "", bool bidirectional = false)
        {
            var edge = Graph.AddEdge(fromId, toId, resistance, tags);
            if (bidirectional)
                Graph.AddEdge(toId, fromId, resistance, tags);
            return edge;
        }

        public Node GetNode(string id) => Graph.GetNode(id);

        public ScalarField AddScalarField(string fieldId, FieldProfile profile)
        {
            var field = new ScalarField(fieldId, profile);
            _fields[fieldId] = field;
            return field;
        }

        /// <summary>
        /// Alias for AddScalarField (for backwards compatibility)
        /// </summary>
        public ScalarField AddField(string fieldId, FieldProfile profile) => AddScalarField(fieldId, profile);

        public ScalarField GetOrCreateScalarField(string fieldId, FieldProfile profile = null)
        {
            if (_fields.TryGetValue(fieldId, out var existing) && existing is ScalarField scalarField)
                return scalarField;

            if (profile == null)
                throw new ArgumentException($"ScalarField '{fieldId}' does not exist and no profile provided");

            return AddScalarField(fieldId, profile);
        }

        public IReadOnlyDictionary<string, Node> Nodes => Graph.Nodes;

        public ScalarField GetScalarField(string fieldId)
        {
            return _fields.TryGetValue(fieldId, out var field) && field is ScalarField scalarField ? scalarField : null;
        }

        public void Clear()
        {
            _fields.Clear();
        }

        public void Step(float dt)
        {
            foreach (var field in _fields.Values)
            {
                if (field is ScalarField scalarField)
                {
                    FieldPropagator.Step(scalarField, Graph, dt);
                }
            }
        }
    }
}
