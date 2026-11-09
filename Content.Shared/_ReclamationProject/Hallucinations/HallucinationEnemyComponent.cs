using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.Hallucinations;

/// <summary>Existing enemy definition and sounds used by a private visual shell.</summary>
[RegisterComponent]
public sealed partial class HallucinationEnemyComponent : Component
{
    [DataField(required: true)] public EntProtoId Source;
    [DataField] public SoundSpecifier? AggroVoice;
    [DataField] public SoundSpecifier? IdleVoice;
    [DataField] public float MinVoiceDelay = 10;
    [DataField] public float MaxVoiceDelay = 60;
    [DataField] public EntProtoId Projectile = "ReclamationPhantomBullet";
}
