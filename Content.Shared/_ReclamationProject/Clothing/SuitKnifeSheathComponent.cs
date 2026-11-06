using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Clothing;

/// <summary>Shows whether a combat knife is present in an armor-mounted item slot.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SuitKnifeSheathComponent : Component
{
    [DataField]
    public string Slot = "chest_knife";
}
