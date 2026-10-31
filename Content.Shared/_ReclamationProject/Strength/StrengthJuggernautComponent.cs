using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;
namespace Content.Shared._ReclamationProject.Strength;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class StrengthJuggernautComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan EndsAt;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUseAt;
}

public sealed partial class StrengthJuggernautActionEvent : InstantActionEvent;
