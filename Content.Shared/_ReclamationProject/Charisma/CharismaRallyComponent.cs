using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared._ReclamationProject.Charisma;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CharismaRallyComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUseAt;
}

public sealed partial class CharismaRallyActionEvent : InstantActionEvent;
