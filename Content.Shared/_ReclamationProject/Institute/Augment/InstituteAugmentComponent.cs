namespace Content.Shared._ReclamationProject.Institute.Augment;

/// <summary>
/// Institute augmentation: a flat cut to all incoming damage, applied on top of armour.
/// Healing is left alone.
/// </summary>
[RegisterComponent]
public sealed partial class InstituteAugmentComponent : Component
{
    /// <summary>
    /// Incoming damage is multiplied by this. 0.85 = 15% less damage.
    /// </summary>
    [DataField]
    public float DamageMultiplier = 0.85f;
}
