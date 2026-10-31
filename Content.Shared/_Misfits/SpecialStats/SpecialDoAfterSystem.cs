using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>Transfers the existing Agility speed curve to timed actions instead of movement.</summary>
public sealed class SpecialDoAfterSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public float GetSpeed(EntityUid user)
    {
        var speed = 1f;
        if (TryComp<SpecialComponent>(user, out var special))
            speed = MathF.Max(0.1f, 1f + _special.GetCurvedEffectModifier(user, SpecialStat.Agility,
                _special.GetTuning().AgilityDoAfterSpeedMultiplierPerPoint, special));
        if (TryComp<DrugSpecialBoostComponent>(user, out var drug) && drug.ExpireTime > _timing.CurTime)
            speed *= 1f + Math.Max(0, drug.AgilityBoost) * 0.015f;
        return speed;
    }

    public TimeSpan GetDelay(EntityUid user, TimeSpan delay) =>
        TimeSpan.FromTicks((long) (delay.Ticks / GetSpeed(user)));
}
