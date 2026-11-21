using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.PowerArmor;

[Serializable, NetSerializable]
public enum ArmorModKind
{
    Overdrive, Jump, Shotgun, Flamer, Deathclaw, Knuckles, Welder, NightVision, MedicalHud, MechanicVisor, AutoInjector,
    JumperCables, MagLock, TowRig, Junkhound
}

/// <summary>A physical module. Its action and tool stay with it when the plate changes hands.</summary>
[RegisterComponent]
public sealed partial class PowerArmorModComponent : Component
{
    [DataField(required: true)] public ArmorModKind Kind;
    [DataField(required: true)] public ArmorPlateSlot Slot;
    [DataField] public EntProtoId? Action;
    [DataField] public EntProtoId? Tool;
    [DataField] public EntityUid? ActionEntity;
    [DataField] public EntityUid? ToolEntity;
    [DataField] public EntityUid? CasualtyDisplay;
    [DataField] public EntityUid? Wearer;
    [DataField] public bool SwitchedOn;
}

[RegisterComponent]
public sealed partial class PowerArmorWristToolComponent : Component
{
    [DataField] public EntityUid Module;
    public bool Moving;
}

[RegisterComponent]
public sealed partial class ArmorCasualtyDisplayComponent : Component
{
    [DataField] public EntityUid Module;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PowerArmorModWearerComponent : Component
{
    [DataField] public HashSet<EntityUid> Modules = new();
    [DataField, AutoNetworkedField] public bool MedicalHud;
    [DataField, AutoNetworkedField] public bool MechanicVisor;
    [DataField, AutoNetworkedField] public bool NightVision;
    [DataField, AutoNetworkedField] public EntityUid? ActiveWristTool;
    public bool ServoJumpActive;
}

public sealed partial class ActivateArmorModEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class ChooseArmorModEvent(NetEntity plate) : EntityEventArgs
{
    public readonly NetEntity Plate = plate;
}

[Serializable, NetSerializable]
public sealed partial class BuildArmorModEvent : DoAfterEvent
{
    [DataField] public ArmorModKind Kind;
    [DataField] public int Version;
    public override DoAfterEvent Clone() => new BuildArmorModEvent { Kind = Kind, Version = Version };
}
