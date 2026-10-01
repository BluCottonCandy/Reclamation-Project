// #Misfits Removed - Replaced by the generic Content.Shared._Misfits.Hallucinations.HallucinationsComponent.
// Kept commented out per the no-delete rule so history is preserved and any external
// references compile-error visibly rather than silently lose behavior.
/*
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.StealthBoy;

[RegisterComponent]
public sealed partial class StealthBoyHallucinationsComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextEvent;

    [DataField]
    public float MinIntervalSeconds = 8f;

    [DataField]
    public float MaxIntervalSeconds = 30f;

    [DataField]
    public ProtoId<Content.Shared.Dataset.LocalizedDatasetPrototype> FlavorDataset = "StealthBoyHallucinations";

    [DataField]
    public ProtoId<Content.Shared.Dataset.LocalizedDatasetPrototype> WhisperDataset = "StealthBoyWhispers";

    [DataField]
    public SoundSpecifier FootstepSound = new SoundCollectionSpecifier("FootstepFloor", AudioParams.Default.WithVolume(-4f));

    [DataField]
    public SoundSpecifier GunshotSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/pistol.ogg", AudioParams.Default.WithVolume(-8f));

    [DataField]
    public SoundSpecifier WhisperSound = new SoundPathSpecifier("/Audio/Effects/clang.ogg", AudioParams.Default.WithVolume(-12f));

    [DataField]
    public DamageSpecifier? BurnoutDamage;
}
*/
