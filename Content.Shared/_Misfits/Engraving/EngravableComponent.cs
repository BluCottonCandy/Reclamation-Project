// #Misfits Add: Marks items that can be permanently engraved by players.
namespace Content.Shared._Misfits.Engraving;

[RegisterComponent]
public sealed partial class EngravableComponent : Component
{
    [DataField]
    public int MaxNameLength = 50;

    [DataField]
    public int MaxDescriptionLength = 1024;
}
