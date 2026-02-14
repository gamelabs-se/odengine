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
        private readonly Dictionary<string, object> _fields;
        public readonly OdNodeGraph Graph;

        public IReadOnlyDictionary<string, object> Fields => _fields;

        public Dimension()
        {
            Graph = new OdNodeGraph();
            _fields = new Dictionary<string, object>();
        }

        public OdNode AddNode(string id, string name = null)
        {
            var node = new OdNode(id, name);
            Graph.AddNode(node);
            return node;
        }

        public OdEdge AddEdge(string fromId, string toId, float resistance = 1.0f, EdgeTags tags = EdgeTags.None, bool bidirectional = false)
        {
            return Graph.AddEdge(fromId, toId, resistance, tags, bidirectional);
        }

        public OdNode GetNode(string id) => Graph.GetNode(id);

        public OdEdge AddEdge(string fromId, string toId, float resistance = 1f, string tags = "")
        {
            return Graph.AddEdge(fromId, toId, resistance, tags);
        }

        public OdField AddField(string fieldId, FieldProfile profile)
        {
            var field = new OdField(fieldId, profile);
            _fields[fieldId] = field;
            return field;
        }

        public ScalarField AddScalarField(string fieldId, FieldProfile profile)
        {
            var field = new ScalarField(fieldId, profile);
            _fields[fieldId] = (OdField)(object)field; // Temporary bridge
            return field;
        }

        public OdField GetOrCreateField(string fieldId, FieldProfile profile = null)
        {
            if (_fields.TryGetValue(fieldId, out var existing) && existing is OdField odField)
                return odField;

            var field = new OdField(fieldId, profile ?? new FieldProfile("default"));
            _fields[fieldId] = field;
            return field;
        }

        public IReadOnlyDictionary<string, OdNode> Nodes => Graph.Nodes;

        public OdField GetField(string fieldId)
        {
            return _fields.TryGetValue(fieldId, out var field) && field is OdField odField ? odField : null;
        }

        public ScalarField GetScalarField(string fieldId)
        {
            return _fields.TryGetValue(fieldId, out var field) && field is ScalarField scalarField ? scalarField : null;
        }

        public void Clear()
        {
            _fields.Clear();
            // Graph has its own clear if needed
        }

        public void Step(float dt)
        {
            // Tick all scalar fields
            foreach (var field in _fields.Values)
            {
                var scalarField = field as ScalarField;
                if (scalarField != null)
                {
                    FieldPropagator.Step(scalarField, Graph, dt);
                }
            }
        }
    }
}
