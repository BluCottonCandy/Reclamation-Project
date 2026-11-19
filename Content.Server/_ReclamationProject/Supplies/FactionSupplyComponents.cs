using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Supplies;
[RegisterComponent]
public sealed partial class FactionSupplyVendorComponent : Component
{
    // Equipment is indexed by the real job on the purchaser's mind, not their ID card.
    [DataField] public bool RequireFactionJob;
    [DataField] public Dictionary<string, int> JobRanks = new();
    [DataField] public Dictionary<string, int> EquipmentRanks = new();
    [DataField] public List<FactionSupplyJobRestriction> RestrictedStock = new();
}

[DataDefinition]
public sealed partial class FactionSupplyJobRestriction
{
    [DataField(required: true)] public HashSet<EntProtoId> Products = new();
    [DataField(required: true)] public HashSet<ProtoId<JobPrototype>> Jobs = new();
}
[RegisterComponent]
public sealed partial class FactionSupplyCrateComponent : Component
{
    [DataField] public int Reward = 20;
}
