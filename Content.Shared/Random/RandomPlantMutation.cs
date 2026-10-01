using Content.Shared.EntityEffects;
using Robust.Shared.Serialization;

namespace Content.Shared.Random;

[Serializable, NetSerializable]
[DataDefinition]
public sealed partial class RandomPlantMutation
{
    [DataField]
    public float BaseOdds = 0f;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public LocId? Description;

    [DataField]
    public EntityEffect Effect = default!;

    [DataField]
    public bool AppliesToProduce = true;

    [DataField]
    public bool AppliesToPlant = true;

    [DataField]
    public bool Persists = true;
}
