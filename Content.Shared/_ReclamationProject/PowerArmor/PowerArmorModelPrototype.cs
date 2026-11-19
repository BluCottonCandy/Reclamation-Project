using Robust.Shared.Prototypes;
namespace Content.Shared._ReclamationProject.PowerArmor;

/// <summary>The ID matches the original assembled suit. Damage coefficients and HP come from that suit.</summary>
[Prototype("powerArmorModel")]
public sealed partial class PowerArmorModelPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public int Rating;
    [DataField(required: true)] public string Sprite = default!;
}
