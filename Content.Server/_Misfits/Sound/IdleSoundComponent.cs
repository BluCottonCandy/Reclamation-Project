// Misfits Change - Component to suppress idle sounds during combat

namespace Content.Server._Misfits.Sound;

/// <summary>
/// When present on an entity with <see cref="Content.Shared.Sound.Components.SpamEmitSoundComponent"/>,
/// suppresses idle sound playback for a configurable duration after the entity attacks.
/// </summary>
[RegisterComponent]
public sealed partial class IdleSoundComponent : Component
{
    /// <summary>
    /// How long (in seconds) to suppress idle sounds after an attack.
    /// </summary>
    [DataField]
    public float CooldownDuration = 15f;

    /// <summary>
    /// Time remaining before idle sounds resume.
    /// </summary>
    [DataField]
    public float CooldownRemaining;

    /// <summary>
    /// Whether idle sounds are currently suppressed due to combat.
    /// </summary>
    [DataField]
    public bool Suppressed;
}
