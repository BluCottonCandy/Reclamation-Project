// #Misfits Add - BarricadeSandbagComponent: placed on built sandbag barricades.
// Tracks material integrity — high sustained damage causes material loss.
// Ported from RMC-14, stripped of marine-specific logic.
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Entrenching;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BarricadeSandbagComponent : Component
{
    /// <summary>
    /// Entity spawned when material is lost (e.g. a sandbag entity to place back).
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId Material = "CMSandbagFull";

    /// <summary>
    /// How many material hits worth of damage can be absorbed before losing a layer.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MaxMaterial = 0;

    /// <summary>
    /// Damage threshold per material unit — every N damage drops one material.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MaterialLossDamageInterval = 75;
}
