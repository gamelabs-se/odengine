using System;

namespace Odengine.Graph
{
    [Serializable]
    public sealed class Node
    {
        public string Id { get; }
        public string Name { get; set; }

        public Node(string id, string name = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Node ID cannot be null or empty", nameof(id));
            
            Id = id;
            Name = name ?? id;
        }

        public override string ToString() => $"Node({Id})";
    }
}
