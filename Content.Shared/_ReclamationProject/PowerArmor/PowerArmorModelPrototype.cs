using Robust.Shared.Prototypes;
namespace Content.Shared._ReclamationProject.PowerArmor;

/// <summary>The ID matches the original assembled suit. Damage coefficients and HP come from that suit.</summary>
[Prototype("powerArmorModel")]
public sealed partial class PowerArmorModelPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public int Rating;
    [DataField(required: true)] public string Sprite = default!;
    /// <summary>Whether this model's plates fit the Atom Cats' T-45 and Raider stencils.</summary>
    [DataField] public bool FitsGarageStencils;
    /// <summary>Stocked replacement parts issued with a factory-assembled suit.</summary>
    [DataField] public Dictionary<ArmorPlateSlot, EntProtoId> Parts = new();
}
