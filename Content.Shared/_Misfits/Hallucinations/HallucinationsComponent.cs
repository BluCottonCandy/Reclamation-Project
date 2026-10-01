// #Misfits Add - Generic hallucinations runtime component.
// Holds the *current* hallucination configuration for an entity. Owners (Stealth Boy,
// Paracusia trait, future systems) write into this via HallucinationsSystem so the
// ticker has a single source of truth. Removed when no source contributes any intensity.
//
// Performance: only entities with this component are iterated by HallucinationsSystem,
// so untouched mobs cost zero.
using Content.Shared.Damage;
using Content.Shared.Dataset;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.Hallucinations;

/// <summary>
/// Runtime state for any source-of-hallucinations effect (Stealth Boy radiation,
/// Paracusia perk, drugs, etc.). Multiple sources can coexist; the highest
/// intensity wins each tick.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HallucinationsComponent : Component
{
    /// <summary>
    /// Effective intensity tier (0-4) used by the ticker. 0 = no events.
    /// Owners set this via HallucinationsSystem.SetIntensity.
    /// </summary>
    [DataField]
    public int Intensity;

    /// <summary>
    /// When the next hallucination event is scheduled to fire.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextEvent;

    /// <summary>
    /// Min/max base seconds between hallucinations at intensity 1. Higher tiers compress this.
    /// </summary>
    [DataField]
    public float MinIntervalSeconds = 12f;

    [DataField]
    public float MaxIntervalSeconds = 40f;

    /// <summary>
    /// Localized dataset of paranoid flavor lines surfaced as small popups.
    /// </summary>
    [DataField]
    public ProtoId<LocalizedDatasetPrototype> FlavorDataset = "StealthBoyHallucinations";

    /// <summary>
    /// Localized dataset of internal-whisper lines (intensity 3+).
    /// </summary>
    [DataField]
    public ProtoId<LocalizedDatasetPrototype> WhisperDataset = "StealthBoyWhispers";

    /// <summary>
    /// Plain dataset of fake nearby-NPC speaker names. Defaults to the shared
    /// human first-names list which is guaranteed to exist.
    /// </summary>
    [DataField]
    public ProtoId<DatasetPrototype> FakeNameDataset = "names_first";

    [DataField]
    public SoundSpecifier FootstepSound = new SoundCollectionSpecifier("FootstepFloor", AudioParams.Default.WithVolume(-4f));

    [DataField]
    public SoundSpecifier GunshotSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/pistol.ogg", AudioParams.Default.WithVolume(-8f));

    [DataField]
    public SoundSpecifier WhisperSound = new SoundPathSpecifier("/Audio/Effects/clang.ogg", AudioParams.Default.WithVolume(-12f));

    /// <summary>
    /// Played during the breakdown tier (intensity 5) when Paracusia + Stealth Boy stack.
    /// Targeted at the affected player only.
    /// </summary>
    [DataField]
    public SoundSpecifier ScreamSound = new SoundCollectionSpecifier("MaleScreams", AudioParams.Default.WithVolume(-2f));

    /// <summary>
    /// True while breakdown effects (glitched text, screams, rapid-fire) are armed.
    /// Toggled by HallucinationsSystem.RefreshIntensity when both Paracusia and an
    /// active Stealth Boy cloak are present on the entity.
    /// </summary>
    [DataField]
    public bool Breakdown;

    /// <summary>
    /// Prototype spawned + parented to the entity during breakdown to apply the
    /// SingularityToy spacetime-distortion overlay. Despawned when breakdown ends.
    /// </summary>
    [DataField]
    public EntProtoId BreakdownDistortionProto = "MisfitsBreakdownDistortion";

    /// <summary>
    /// Live distortion entity tracked so we can clean it up when breakdown ends.
    /// Not networked - server-only bookkeeping.
    /// </summary>
    [ViewVariables]
    public EntityUid? BreakdownDistortion;

    /// <summary>
    /// Blur magnitude applied to the player while breakdown is active (crit-state
    /// vignette). Pulled into Content.Shared.Eye.Blinding via GetBlurEvent.
    /// </summary>
    [DataField]
    public float BreakdownBlur = BlurryVisionMaxMagnitude;

    public const float BlurryVisionMaxMagnitude = 6f;

    /// <summary>
    /// Damage dealt per tick at intensity 4 (lore-accurate brain damage).
    /// Null disables damage entirely (used by Paracusia perk — it shouldn't physically harm).
    /// </summary>
    [DataField]
    public DamageSpecifier? BurnoutDamage;

    /// <summary>
    /// Optional list of phantom entity prototypes spawned client-side for the affected
    /// player (intensity 3+). Empty = no phantom visuals fired.
    /// </summary>
    [DataField]
    public List<EntProtoId> PhantomPrototypes = new();

    /// <summary>
    /// Lifetime in seconds for phantom entities spawned client-side.
    /// </summary>
    [DataField]
    public float PhantomLifetime = 1.5f;

    /// <summary>
    /// Max tile distance from the affected entity to spawn a phantom.
    /// </summary>
    [DataField]
    public int PhantomMaxRange = 5;
}
