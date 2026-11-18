// #Reclamation Project - medieval weapons: crossbow and sling show their loaded sprite (world and in hand).
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Medieval;

/// <summary>
/// While <see cref="Container"/> holds something, the sprite shows <see cref="State"/> and the in-hand
/// sprites use the "{State}-inhand-left/right" states. Read by a GenericVisualizer on <see cref="MedievalLookVisuals.State"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MedievalLoadedLookComponent : Component
{
    [DataField(required: true)]
    public string Container = string.Empty;

    [DataField]
    public string State = "loaded";
}
