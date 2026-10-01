// #Misfits Change Add - Marks the target of a double-grab. Present during both Pending and Active phases.
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Misfits.Grabbing.Components;

/// <summary>
/// Added to the victim when a double-grab wind-up begins.
/// Removed when the grab is cancelled or the active carry ends.
/// </summary>
[RegisterComponent]
public sealed partial class BeingDoubleGrabbedComponent : Component
{
    [DataField]
    public EntityUid Carrier;

    /// <summary>
    /// How long the victim has to break out of the active carry via EscapeInventorySystem.
    /// </summary>
    [DataField]
    public TimeSpan EscapeTime = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan NextGaspEmoteTime;

    [DataField]
    public TimeSpan GaspEmoteCooldown = TimeSpan.FromSeconds(4);
}
