using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.PowerArmor;

public enum ArmorPlateSlot : byte { Pauldrons, Chest, Gloves, Helmet }

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ModularPowerArmorComponent : Component
{
    [DataField] public bool StartsEmpty;
    [DataField] public int ModCapacity = 1;
    [DataField] public string FrameSprite = "_ReclamationProject/Clothing/ModularPowerArmor/frame.rsi";
    [DataField, AutoNetworkedField] public bool Overdrive;
    [DataField("initialized")] public bool FactoryPartsCreated;
    [DataField, AutoNetworkedField] public int Rating = 20;
    [DataField, AutoNetworkedField] public float Absorption;
    [DataField, AutoNetworkedField] public bool Sealed;
    [DataField, AutoNetworkedField] public Dictionary<ArmorPlateSlot, string> Visuals = new();
    [DataField, AutoNetworkedField] public bool PaintedHelmet;
    [DataField, AutoNetworkedField] public Dictionary<ArmorPlateSlot, ArmorModKind> ModVisuals = new();
}

/// <summary>Combined condition of the equipped plates, visible to diagnostic visors on other players.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WornArmorConditionComponent : Component
{
    [DataField, AutoNetworkedField] public float Fraction;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PowerArmorPlateComponent : Component
{
    [DataField(required: true)] public ArmorPlateSlot Slot;
    [DataField, AutoNetworkedField] public EntProtoId Model = "N14ClothingOuterPowerArmorT45";
    [DataField, AutoNetworkedField] public string? PaintFinish;
    [DataField, AutoNetworkedField] public int HitPoints;
    [DataField] public ArmorModKind? Project;
    [DataField, AutoNetworkedField] public ArmorModKind? FittedMod;
    [DataField] public int BuildStep;
    [DataField] public int BuildVersion;
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
