using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.GunAttachments;

[RegisterComponent, NetworkedComponent]
public sealed partial class GunSuppressorComponent : Component
{
    [DataField] public float SoundReduction = 12f;
    [DataField] public float VelocityMultiplier = 0.9f;
    [DataField] public bool HideMuzzleFlash = true;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class GunGripComponent : Component
{
    [DataField] public float RecoilMultiplier = 0.7f;
}

/// <summary>RMC-style gun-to-attachment event relay, adapted to the existing N14 gun events.</summary>
[ByRefEvent]
public struct GunAttachmentRelayedEvent<T>
{
    public EntityUid Holder;
    public T Args;
    public GunAttachmentRelayedEvent(EntityUid holder, T args) { Holder = holder; Args = args; }
}

[ByRefEvent]
public record struct GunAttachmentChangedEvent(string SlotId, EntityUid Item, bool Inserted);

public sealed partial class ToggleWeaponLightActionEvent : InstantActionEvent { }

[RegisterComponent, NetworkedComponent]
public sealed partial class GunStockComponent : Component
{
    [DataField] public float ControlMultiplier = 0.7f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class GunLaserSightComponent : Component
{
    [DataField] public float AccuracyMultiplier = 0.7f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class GunBipodComponent : Component { }

public sealed partial class ToggleGunBipodActionEvent : InstantActionEvent { }

[RegisterComponent, NetworkedComponent]
public sealed partial class GunHoloSightComponent : Component
{
    [DataField] public float AccuracyMultiplier = 0.9f;
}
