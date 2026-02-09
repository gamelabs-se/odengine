namespace Odengine.Economy
{
    /// <summary>
    /// Intent to buy or sell an item at a node.
    /// Processed by EconomyEngine to modify field amplitudes.
    /// </summary>
    public struct TradeIntent
    {
        public string NodeId;
        public string ItemId;
        public int Quantity;
        public bool IsBuy; // true = buy, false = sell
    }
}
