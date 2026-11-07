using Content.Shared.Mobs;
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Endurance;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class EnduranceEnragedComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField, AutoNetworkedField]
    public TimeSpan EndsAt;

    [DataField, AutoNetworkedField]
    public TimeSpan ReadyAt;

    // Restored by the server when the temporary stamina pool ends.
    public float? PreviousStaminaThreshold;

    // Only the server resolves direct state changes on expiry.
    public MobState? ForcedState;
}
