// #Misfits Add - FullSandbagComponent: placed on filled/full sandbag items.
// Use an entrenching tool to assemble full bags into a barricade structure.
// Ported from RMC-14, stripped of marine-specific logic.
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Entrenching;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FullSandbagComponent : Component
{
    /// <summary>
    /// How long it takes to build one layer of barricade from full bags.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan BuildDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Number of full bags required to build one barricade layer.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int StackRequired = 5;

    /// <summary>
    /// Entity spawned as the completed barricade.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId Builds = "CMBarricadeSandbag";
}
