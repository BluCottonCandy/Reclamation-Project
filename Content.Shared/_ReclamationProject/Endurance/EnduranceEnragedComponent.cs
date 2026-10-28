using Content.Shared.Mobs;
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Endurance;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EnduranceEnragedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField, AutoNetworkedField]
    public TimeSpan EndsAt;

    [DataField, AutoNetworkedField]
    public TimeSpan ReadyAt;

    // Only the server resolves direct state changes on expiry.
    public MobState? ForcedState;
}
