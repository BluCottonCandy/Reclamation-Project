using Robust.Shared.Network;

namespace Content.Server._ReclamationProject.Forged;

[RegisterComponent]
public sealed partial class ForgedBrandingIronComponent : Component
{
    [DataField] public string Brand = "ForgedAnvilBrand";
    [DataField] public bool Unbrand;
}

// Captured when this body spawns: never derive the victim's slot from the attacker's preferences.
[RegisterComponent]
public sealed partial class ForgedBrandProfileComponent : Component
{
    public NetUserId User;
    public int Slot;
    public string CharacterName = string.Empty;
}

// Local test fixtures only. Ordinary NPCs are not persistently branded.
[RegisterComponent]
public sealed partial class ForgedBrandingPracticeComponent : Component
{
    [DataField] public string? StartingBrand;
}
