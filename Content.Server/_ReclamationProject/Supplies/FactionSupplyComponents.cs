namespace Content.Server._ReclamationProject.Supplies;
[RegisterComponent]
public sealed partial class FactionSupplyVendorComponent : Component
{
    // Equipment is indexed by the real job on the purchaser's mind, not their ID card.
    [DataField] public bool RequireFactionJob;
    [DataField] public Dictionary<string, int> JobRanks = new();
    [DataField] public Dictionary<string, int> EquipmentRanks = new();
}
[RegisterComponent]
public sealed partial class FactionSupplyCrateComponent : Component
{
    [DataField] public int Reward = 20;
}
