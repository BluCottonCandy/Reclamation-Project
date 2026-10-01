// Marker component placed on player entities who have the Power Armor Training perk.
// Without this, equipping any item tagged with N14PowerArmorComponent will be blocked.

namespace Content.Shared._Misfits.PowerArmor;

/// <summary>
///     Granted to a character via the N14PowerArmorTraining trait.
///     Required in order to equip power armor (items bearing <see cref="N14PowerArmorComponent"/>).
///     Add via TraitAddComponent in YAML — do NOT add directly in job specials unless the
///     role is intended to bypass character-creation perk selection (e.g. the BoS Paladin).
/// </summary>
[RegisterComponent]
public sealed partial class PowerArmorProficiencyComponent : Component
{
}
