using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.VendingMachines
{
    [Serializable, NetSerializable, Prototype("vendingMachineInventory")]
    public sealed partial class VendingMachineInventoryPrototype : IPrototype
    {
        [ViewVariables]
        [IdDataField]
        public string ID { get; private set; } = default!;

        [DataField("startingInventory")]
        public Dictionary<string, uint> StartingInventory { get; private set; } = new();

        [DataField("emaggedInventory")]
        public Dictionary<string, uint>? EmaggedInventory { get; private set; }

        [DataField("contrabandInventory")]
        public Dictionary<string, uint>? ContrabandInventory { get; private set; }
    }
}
