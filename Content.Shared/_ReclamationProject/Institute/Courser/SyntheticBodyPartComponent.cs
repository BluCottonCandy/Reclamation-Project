using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Institute.Courser;

/// <summary>
/// A Courser body part or organ. It passes for human while it's in a body; once it comes out
/// (cut off, gibbed, removed in surgery) its name and description give it away as synthetic.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SyntheticBodyPartComponent : Component
{
    [DataField(required: true)]
    public LocId RevealedName;

    [DataField(required: true)]
    public LocId RevealedDescription;

    [ViewVariables]
    public bool Revealed;
}
