// #Reclamation Project - medieval weapons: special heavy (wide) swings for the weapons with more than one mode.
namespace Content.Shared._ReclamationProject.Medieval;

/// <summary>
/// Extra effects on everyone hit by this weapon's heavy (right-click, wide) swing.
/// With <see cref="RequireActive"/> they only happen while the weapon is switched on (lit, charged, open, flipped...).
/// </summary>
[RegisterComponent]
public sealed partial class MedievalHeavyAttackComponent : Component
{
    [DataField]
    public bool RequireActive = true;

    /// <summary>Fire stacks put on each target (and lights them).</summary>
    [DataField]
    public float IgniteStacks;

    /// <summary>Fire stacks instead of <see cref="IgniteStacks"/> while the weapon is wielded.</summary>
    [DataField]
    public float WieldedIgniteStacks;

    /// <summary>Seconds each target is knocked down.</summary>
    [DataField]
    public float Knockdown;

    /// <summary>Seconds each target is slowed, and to what fraction of their speed.</summary>
    [DataField]
    public float Slow;

    [DataField]
    public float SlowMultiplier = 0.5f;

    /// <summary>Bleeding added to each target.</summary>
    [DataField]
    public float Bleed;
}
