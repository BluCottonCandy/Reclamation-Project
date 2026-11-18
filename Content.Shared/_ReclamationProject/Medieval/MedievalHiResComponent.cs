// #Reclamation Project - medieval weapons: in-hand and worn sprites drawn from the full-detail art.
namespace Content.Shared._ReclamationProject.Medieval;

/// <summary>
/// The item's RSI is drawn at double resolution (64px frames for a 32px tile). The client draws its
/// in-hand and worn layers at <see cref="Scale"/>, so they keep every pixel of the original art,
/// matching the floor sprite (which uses the same art with Sprite scale 0.5).
/// </summary>
[RegisterComponent]
public sealed partial class MedievalHiResComponent : Component
{
    [DataField]
    public float Scale = 0.5f;
}
