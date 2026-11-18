// #Reclamation Project - medieval weapons: blacksmith-only recipes on a workbench.
using Content.Shared.Lathe.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.Medieval;

/// <summary>
/// Adds every lathe recipe in <see cref="Categories"/> to this workbench.
/// Anyone can see them, but only a character with <see cref="BlacksmithComponent"/> can queue them.
/// </summary>
[RegisterComponent]
public sealed partial class BlacksmithLatheComponent : Component
{
    [DataField]
    public List<ProtoId<LatheCategoryPrototype>> Categories = new() { "RPBlacksmithWeapons", "RPBlacksmithArmor" };

    [DataField]
    public LocId DeniedPopup = "rp-blacksmith-denied";
}
