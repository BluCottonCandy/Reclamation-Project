// #Misfits Add - EmptySandbagComponent: placed on sandbag items before they are filled with dirt.
// Use an entrenching tool to fill an empty bag at a dirt/sand tile.
// Ported from RMC-14, stripped of marine-specific logic.
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Entrenching;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmptySandbagComponent : Component
{
    /// <summary>
    /// Entity this empty bag becomes after being filled.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId Filled = "CMSandbagFull";
}
