using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Pack;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunAgilityRequirementComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Minimum = 8;
}
