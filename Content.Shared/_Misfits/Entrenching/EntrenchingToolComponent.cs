// #Misfits Add - EntrenchingToolComponent: placed on shovel-type items used to dig, fill, and build barricades.
// Ported from RMC-14 — RMC-specific references removed; uses standard SS14 audio.
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Entrenching;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EntrenchingToolComponent : Component
{
    // --- Dig ---

    /// <summary>
    /// How long it takes to dig one batch of empty sandbags from the ground.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan DigDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Number of empty sandbag items created per dig.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int LayersPerDig = 5;

    /// <summary>
    /// Maximum number of empty bags that can be produced at one location
    /// before the ground becomes depleted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int TotalLayers = 20;

    // --- Fill ---

    /// <summary>
    /// How long it takes to fill a single empty sandbag with dirt.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan FillDelay = TimeSpan.FromSeconds(3);

    // --- Audio ---

    [DataField, AutoNetworkedField]
    public SoundSpecifier DigSound = new SoundPathSpecifier("/Audio/Items/shovel_dig.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier FillSound = new SoundPathSpecifier("/Audio/Items/shovel_dig.ogg");
}
