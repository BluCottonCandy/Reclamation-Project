using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared._ReclamationProject.Agility;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class AgilityFastHandsComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Action;
    [DataField, AutoNetworkedField, AutoPausedField] public TimeSpan NextUseAt;
    [DataField, AutoNetworkedField] public bool Reloading;
    [DataField] public EntityUid? ReloadTarget;
    [DataField] public EntityUid? ReloadGun;
    [DataField] public DoAfterId? DoAfter;
    [DataField] public int RoundsLoaded;
    [DataField] public int RoundsRemaining;
}

public sealed partial class AgilityFastHandsActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class FastHandsReloadDoAfterEvent : SimpleDoAfterEvent;
