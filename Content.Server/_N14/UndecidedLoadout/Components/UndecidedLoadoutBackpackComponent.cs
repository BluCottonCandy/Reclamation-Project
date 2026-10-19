using Content.Server.UndecidedLoadout.Systems;
using Content.Shared.UndecidedLoadout;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server.UndecidedLoadout.Components;

/// <summary>
/// This component stores the possible contents of the backpack,
/// which can be selected via the interface.
/// </summary>
[RegisterComponent, Access(typeof(UndecidedLoadoutBackpackSystem))]
public sealed partial class UndecidedLoadoutBackpackComponent : Component
{
    /// <summary>
    /// List of sets available for selection
    /// </summary>
    [DataField]
    public List<ProtoId<UndecidedLoadoutBackpackSetPrototype>> PossibleSets = new();

    /// <summary>Number of distinct sets required before redeeming this kit.</summary>
    [DataField]
    public int MaxSelectedSets = 1;

    [DataField]
    public List<int> SelectedSets = new();

    [DataField]
    public SoundSpecifier ApproveSound = new SoundPathSpecifier("/Audio/Effects/rustle1.ogg");
}
