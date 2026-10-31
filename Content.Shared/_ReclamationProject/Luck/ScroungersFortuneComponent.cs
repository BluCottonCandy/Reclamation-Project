using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._ReclamationProject.Luck;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ScroungersFortuneComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid? Action;
    [DataField, AutoNetworkedField, AutoPausedField] public TimeSpan NextUseAt;
    [DataField, AutoNetworkedField] public bool Searching;
    public DoAfterId? DoAfter;
    public const float Radius = 4f;
}

public sealed partial class ScroungersFortuneActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class ScroungersFortuneDoAfterEvent : SimpleDoAfterEvent;
