namespace Odengine.Economy
{
    /// <summary>
    /// Base definition for an item.
    /// Odengine only needs to know: ID, name, base value.
    /// Game layer can extend this with weapons, armor, etc.
    /// </summary>
    public sealed class ItemDef
    {
        public string ItemId { get; }
        public string DisplayName { get; }
        public float BaseValue { get; }

        public ItemDef(string itemId, string displayName, float baseValue)
        {
            ItemId = itemId;
            DisplayName = displayName;
            BaseValue = baseValue;
        }
    }
}
