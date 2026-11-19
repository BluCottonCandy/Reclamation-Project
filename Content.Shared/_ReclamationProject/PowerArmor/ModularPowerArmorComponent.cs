using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.PowerArmor;

public enum ArmorPlateSlot : byte { Pauldrons, Chest, Gloves, Helmet }

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ModularPowerArmorComponent : Component
{
    [DataField] public bool StartsEmpty;
    [DataField("initialized")] public bool FactoryPartsCreated;
    [DataField, AutoNetworkedField] public int Rating = 20;
    [DataField, AutoNetworkedField] public float Absorption;
    [DataField, AutoNetworkedField] public bool Sealed;
    [DataField, AutoNetworkedField] public Dictionary<ArmorPlateSlot, string> Visuals = new();
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PowerArmorPlateComponent : Component
{
    [DataField(required: true)] public ArmorPlateSlot Slot;
    [DataField, AutoNetworkedField] public EntProtoId Model = "N14ClothingOuterPowerArmorT45";
    [DataField, AutoNetworkedField] public int HitPoints;
    [DataField, AutoNetworkedField] public int Rating;
}

/// <summary>The chest assembly includes the legs, and each other slot takes one complete pair or helmet.</summary>
public static class PowerArmorPlates
{
    public static readonly ArmorPlateSlot[] Slots = Enum.GetValues<ArmorPlateSlot>();
    public static string Container(ArmorPlateSlot slot) => "plate-" + slot.ToString().ToLowerInvariant();
    public static float Share(ArmorPlateSlot slot) => slot switch
    {
        ArmorPlateSlot.Chest => 0.48f,
        ArmorPlateSlot.Gloves => 0.12f,
        _ => 0.20f
    };

    public static int Portion(int total, ArmorPlateSlot slot)
    {
        int Part(ArmorPlateSlot s) => (int) MathF.Round(total * Share(s));
        return slot == ArmorPlateSlot.Chest
            ? total - Part(ArmorPlateSlot.Pauldrons) - Part(ArmorPlateSlot.Gloves) - Part(ArmorPlateSlot.Helmet)
            : Part(slot);
    }
}
