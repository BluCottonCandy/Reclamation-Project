// #Reclamation Project Add - Guns whose recoil breaks the firing arm of weak shooters (Thunder .50 pistol).
using Content.Shared.Whitelist;

namespace Content.Shared._ReclamationProject.Weapons.Ranged;

/// <summary>
/// When a shooter with less than <see cref="MinStrength"/> effective Strength fires this gun,
/// the arm holding it is fractured through the trauma system.
/// </summary>
[RegisterComponent]
public sealed partial class RecoilArmBreakComponent : Component
{
    /// <summary>
    /// Effective SPECIAL Strength needed to fire without breaking your arm.
    /// </summary>
    [DataField]
    public int MinStrength = 7;

    /// <summary>
    /// Shooters matching this whitelist never break their arm, whatever their Strength
    /// (e.g. Coursers, whose synthetic frames shrug off the recoil).
    /// </summary>
    [DataField]
    public EntityWhitelist? Exempt;

    /// <summary>
    /// Message shown to the shooter when the recoil breaks their arm.
    /// </summary>
    [DataField]
    public LocId BreakPopup = "recoil-arm-break-self";

    /// <summary>
    /// Message shown to everyone else nearby.
    /// </summary>
    [DataField]
    public LocId BreakPopupOthers = "recoil-arm-break-others";
}
