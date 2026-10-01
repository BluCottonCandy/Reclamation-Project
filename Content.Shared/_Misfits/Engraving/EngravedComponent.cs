// #Misfits Add: Stores the one-time engraving owner shown on examine.
namespace Content.Shared._Misfits.Engraving;

[RegisterComponent]
public sealed partial class EngravedComponent : Component
{
    [DataField]
    public string OwnerName = string.Empty;
}
