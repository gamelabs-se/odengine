using System;

namespace Odengine.Economy
{
    [Serializable]
    public sealed class ItemDef
    {
        public string Id { get; }
        public string Name { get; set; }
        public float BaseValue { get; set; }

        public ItemDef(string id, string name, float baseValue)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("ItemDef ID cannot be null or empty", nameof(id));
            
            Id = id;
            Name = name ?? id;
            BaseValue = baseValue;
        }
    }
}
