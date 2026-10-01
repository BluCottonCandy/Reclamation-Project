// #Misfits Change - Periodically emits sparks and spark sounds for heavily damaged player robots.
using Robust.Shared.Audio;

namespace Content.Shared._Misfits.Silicon;

/// <summary>
/// Adds intermittent spark effects while a robot is close to destruction.
/// The effect naturally stops once damage drops below the configured threshold.
/// </summary>
[RegisterComponent]
public sealed partial class RobotCriticalSparkingComponent : Component
{
    [DataField]
    public float ActivationThreshold = 0.75f;

    [DataField]
    public float CycleDelay = 1.5f;

    [DataField]
    public TimeSpan SparkCooldown = TimeSpan.FromSeconds(3);

    [DataField]
    public SoundSpecifier Sound = new SoundCollectionSpecifier("sparks");

    [ViewVariables]
    public TimeSpan LastSparkTime;

    [ViewVariables]
    public float AccumulatedFrametime;
}