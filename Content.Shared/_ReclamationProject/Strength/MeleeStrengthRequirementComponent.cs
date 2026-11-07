using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Strength;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MeleeStrengthRequirementComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Minimum = 8;
}
