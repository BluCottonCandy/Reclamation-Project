using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Burial.Components;

/// <summary>
/// When added to an item (e.g. N14Shovel), lets the holder dig a brand-new grave on
/// an empty tile. After spawning, the grave is immediately opened so a body can be
/// placed inside; closing it again uses the standard BurialSystem flow.
/// </summary>
[RegisterComponent]
public sealed partial class GraveCreatorComponent : Component
{
    /// <summary>
    /// Entity prototype to spawn as the freshly-dug grave.
    /// </summary>
    [DataField]
    public EntProtoId GravePrototype = "CrateWoodenGrave";

    /// <summary>
    /// Base time to dig the grave before any speed modifiers.
    /// </summary>
    [DataField]
    public TimeSpan DigDelay = TimeSpan.FromSeconds(15f);

    /// <summary>
    /// Looping sound played while digging.
    /// </summary>
    [DataField]
    public SoundPathSpecifier DigSound = new SoundPathSpecifier("/Audio/Nyanotrasen/Items/shovel_dig.ogg")
    {
        Params = AudioParams.Default.WithLoop(true)
    };

    /// <summary>
    /// Currently-playing looping audio stream entity. Null when idle.
    /// </summary>
    [DataField]
    public EntityUid? Stream;

    /// <summary>
    /// True while a grave-creation doAfter is in progress, to prevent double-starting.
    /// </summary>
    [DataField]
    public bool IsDigging;
}
