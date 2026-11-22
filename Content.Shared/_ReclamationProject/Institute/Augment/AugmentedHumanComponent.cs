using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Institute.Augment;

/// <summary>
/// #Reclamation Project - marks an Augmented Human (the Director's race). Whatever job they spawn as, they always
/// also count as Institute.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentedHumanComponent : Component
{
    [DataField]
    public string Faction = "Institute";
}
