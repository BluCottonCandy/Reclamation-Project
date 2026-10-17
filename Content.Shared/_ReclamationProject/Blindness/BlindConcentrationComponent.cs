using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Blindness;

/// <summary>Granted only by the N14 blindness trait; does not restore eyesight.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BlindConcentrationComponent : Component
{
    public const float BuildSeconds = 8f;
    public const float FadeSeconds = 4f;

    [DataField, AutoNetworkedField]
    public bool ClosedEyeBonus;

    public float BuildDuration => ClosedEyeBonus ? BuildSeconds - 1f : BuildSeconds;
    public float FadeDuration => ClosedEyeBonus ? FadeSeconds + 2f : FadeSeconds;
    public float MaximumRadiusMultiplier => ClosedEyeBonus ? 6f : 3f;

    [DataField, AutoNetworkedField]
    public bool Concentrating;

    [DataField, AutoNetworkedField]
    public TimeSpan TransitionStart;

    [DataField, AutoNetworkedField]
    public float StartProgress;

    [DataField]
    public EntityUid? Action;

    public DoAfterId? DoAfter;

    public float Progress(TimeSpan now)
    {
        var elapsed = Math.Max(0f, (float) (now - TransitionStart).TotalSeconds);
        return Math.Clamp(StartProgress + (Concentrating ? elapsed / BuildDuration : -elapsed / FadeDuration), 0f, 1f);
    }
}

public sealed partial class BlindConcentrateActionEvent : InstantActionEvent { }

[Serializable, NetSerializable]
public sealed partial class BlindConcentrateDoAfterEvent : SimpleDoAfterEvent { }
