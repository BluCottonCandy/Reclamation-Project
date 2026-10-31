using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Intelligence;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class IntelligenceOverrideComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    [DataField, AutoNetworkedField]
    public bool Selected;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUseAt;
}

public sealed partial class IntelligenceOverrideActionEvent : EntityTargetActionEvent;

/// <summary>Marks an AI robot reprogrammed to protect players without becoming a follower.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OverriddenRobotComponent : Component;

[Serializable, NetSerializable]
public sealed class OverrideVisualSelectionEvent(bool selected) : EntityEventArgs
{
    public bool Selected = selected;
}
