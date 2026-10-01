// #Misfits Add - Tracks a player's cumulative Stealth Boy "stealth radiation" exposure.
// Persists across activations so that long-term users gradually develop hallucinations,
// paranoia, and finally physical brain damage. Decays slowly while not in use.
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.StealthBoy;

/// <summary>
/// Applied to a user the first time they activate any Stealth Boy. Tracks the
/// running exposure value used to scale hallucinations and damage tiers.
/// Persists across cloak activations and decays slowly while inactive.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StealthBoyExposureComponent : Component
{
    /// <summary>
    /// Total accumulated exposure in seconds. Goes up while a Stealth Boy is active,
    /// decays slowly when not. Tier thresholds compare against this.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ExposureSeconds;

    /// <summary>
    /// How much exposure decays per real-time second when not actively cloaked.
    /// 0.05 means a 90s session lingers ~30 minutes before fully clearing.
    /// </summary>
    [DataField]
    public float DecayPerSecond = 0.05f;

    /// <summary>
    /// Cached current tier (0-4) so we can detect transitions and update alerts/popups.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CurrentTier;

    /// <summary>
    /// Tier exposure thresholds in seconds. Index = tier number.
    /// 0 entry unused; tier 1 = 30s buzz, 2 = 90s paranoia, 3 = 180s schizophrenia, 4 = 360s burnout.
    /// </summary>
    [DataField]
    public float[] TierThresholds = { 0f, 30f, 90f, 180f, 360f };

    /// <summary>
    /// When exposure last changed; used to throttle decay updates.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan LastUpdate;
}
