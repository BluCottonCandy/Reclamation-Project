// #Misfits Change - Drives spark visuals and audio for player robots near destruction.
using Content.Shared._Misfits.Silicon;
using Content.Shared.Audio;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// Emits intermittent sparks and spark sounds while a robot is above a configurable fraction
/// of its death threshold, then stops once repairs bring it back below that threshold.
/// </summary>
public sealed class RobotCriticalSparkingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    // Misfits Fix: sparks fire on CycleDelay intervals (not every tick); run the outer scan
    // at 10 Hz instead of 20 Hz — halves entity-query overhead with no visible difference.
    private float _accumTimer;
    private const float UpdateInterval = 0.1f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Misfits Fix: gate outer query to 10 Hz — spark timing already uses AccumulatedFrametime,
        // so dt is passed through and precision is preserved.
        _accumTimer += frameTime;
        if (_accumTimer < UpdateInterval)
            return;
        var dt = _accumTimer;
        _accumTimer = 0f;

        var query = EntityQueryEnumerator<RobotCriticalSparkingComponent, MobStateComponent, MobThresholdsComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var component, out var mobState, out var thresholds, out var damageable))
        {
            if (_mobState.IsDead(uid, mobState) ||
                !_mobThreshold.TryGetThresholdForState(uid, MobState.Dead, out var deathThreshold, thresholds))
            {
                component.AccumulatedFrametime = 0f;
                continue;
            }

            if (damageable.TotalDamage < deathThreshold * component.ActivationThreshold)
            {
                component.AccumulatedFrametime = 0f;
                continue;
            }

            component.AccumulatedFrametime += dt;
            if (component.AccumulatedFrametime < component.CycleDelay)
                continue;

            component.AccumulatedFrametime -= component.CycleDelay;

            if (_timing.CurTime <= component.LastSparkTime + component.SparkCooldown)
                continue;

            component.LastSparkTime = _timing.CurTime;
            Spawn("EffectSparks", Transform(uid).Coordinates);
            _audio.PlayPvs(component.Sound, uid, AudioHelpers.WithVariation(0.05f, _random));
        }
    }
}