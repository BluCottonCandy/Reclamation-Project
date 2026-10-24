using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Institute.Courser;

/// <summary>
/// Marks a Courser (Institute Gen 3 synth). Whatever job they spawn as, they always also count as Institute.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CourserSynthComponent : Component
{
    [DataField]
    public string Faction = "Institute";
}
