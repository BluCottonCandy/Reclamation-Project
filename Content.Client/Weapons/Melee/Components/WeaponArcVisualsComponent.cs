namespace Content.Client.Weapons.Melee.Components;

/// <summary>
/// Used for melee attack animations. Typically just has a fadeout.
/// </summary>
[RegisterComponent]
public sealed partial class WeaponArcVisualsComponent : Component
{
    public EntityUid? User;

    [DataField("animation")]
    public WeaponArcAnimation Animation = WeaponArcAnimation.None;

    [ViewVariables(VVAccess.ReadWrite), DataField("fadeOut")]
    public bool Fadeout = true;

    /// <summary>Scales visual timing without changing attack rate or hit detection.</summary>
    [DataField]
    public float DurationMultiplier = 1f;
}

public enum WeaponArcAnimation : byte
{
    None,
    Thrust,
    Slash,
}
