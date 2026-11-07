using Robust.Shared.GameStates;

namespace Content.Shared.Item;

/// <summary>Uses the item's existing icon as an upright pose when carried in one hand.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OneHandedUprightItemComponent : Component
{
    [DataField("sprite", required: true)]
    public string Sprite = default!;

    [DataField]
    public string State = "icon";

    [DataField]
    public float Scale = 0.65f;

    [DataField]
    public float Rotation = 45f;
}
