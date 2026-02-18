using System;
using System.Collections.Generic;
using Odengine.Fields;
using Odengine.Graph;

namespace Odengine.Core
{
    /// <summary>
    /// Container for the simulation: nodes, edges, fields.
    /// No Unity dependencies.
    /// </summary>
    [Serializable]
    public sealed class Dimension
    {
        public NodeGraph Graph { get; } = new NodeGraph();
        
        private readonly Dictionary<string, ScalarField> _fields = new Dictionary<string, ScalarField>(StringComparer.Ordinal);
        
        public IReadOnlyDictionary<string, ScalarField> Fields => _fields;

        public Node AddNode(string id, string name = null)
        {
            var node = new Node(id, name);
            Graph.AddNode(node);
            return node;
        }

        public void AddEdge(string fromId, string toId, float resistance, params string[] tags)
        {
            Graph.AddEdge(fromId, toId, resistance, tags);
        }

        public ScalarField AddField(string fieldId, FieldProfile profile)
        {
            if (_fields.ContainsKey(fieldId))
                throw new InvalidOperationException($"Field '{fieldId}' already exists");

            var field = new ScalarField(fieldId, profile);
            _fields[fieldId] = field;
            return field;
        }

        public ScalarField GetField(string fieldId)
        {
            return _fields.TryGetValue(fieldId, out var field) ? field : null;
        }

        public ScalarField GetOrCreateField(string fieldId, FieldProfile profile)
        {
            if (_fields.TryGetValue(fieldId, out var existing))
                return existing;

            return AddField(fieldId, profile);
        }
    }
}
