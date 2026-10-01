using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Standing;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LayingDownComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan StandingUpTime = TimeSpan.FromSeconds(1);

    // Misfits Add: recovery time used when getting up after exiting hard crit (collapsed from injuries)
    [DataField, AutoNetworkedField]
    public TimeSpan CritStandingUpTime = TimeSpan.FromSeconds(8);

    // Misfits Add: recovery time used when getting up after exiting soft crit (stim recovery)
    [DataField, AutoNetworkedField]
    public TimeSpan SoftCritStandingUpTime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Misfits Add: set to the appropriate recovery duration when the entity exits crit/soft-crit.
    /// Null means normal stand-up time applies. Cleared once the DoAfter resolves.
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan? PostCritRecoveryOverride = null;

    [DataField, AutoNetworkedField]
    public float LyingSpeedModifier = 0.10f, // Corvax-Change
                 CrawlingUnderSpeedModifier = 0.5f;

    // Misfits Add: speed modifier applied when crawling while in Critical mob state.
    // This stacks with LyingSpeedModifier (e.g. 0.05 * 0.10 = 0.5% of base speed).
    // Set to 1.0 to use normal lying speed instead.
    [DataField, AutoNetworkedField]
    public float CritCrawlSpeedModifier = 0.5f;

    [DataField, AutoNetworkedField]
    public bool AutoGetUp;

    /// <summary>
    ///     If true, the entity is choosing to crawl under furniture. This is purely visual and has no effect on physics.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsCrawlingUnder = false;

    [DataField, AutoNetworkedField]
    public int NormalDrawDepth = (int) DrawDepth.DrawDepth.Mobs,
               CrawlingUnderDrawDepth = (int) DrawDepth.DrawDepth.Mobs; /// Switching between drawdepths in-game tends to glitch, so let's keep the two the same
}

[Serializable, NetSerializable]
public sealed class ChangeLayingDownEvent : CancellableEntityEventArgs;

[Serializable, NetSerializable]
public sealed class CheckAutoGetUpEvent(NetEntity user) : CancellableEntityEventArgs
{
    public NetEntity User = user;
}
