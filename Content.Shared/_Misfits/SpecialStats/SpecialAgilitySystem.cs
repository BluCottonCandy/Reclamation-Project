using Content.Shared._Misfits.Special;
using Content.Shared._ReclamationProject.Endurance;
using Content.Shared.Item;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>
/// Applies Agility to close-quarters action cadence.
/// </summary>
public sealed class SpecialAgilitySystem : EntitySystem
{
    [Dependency] private readonly EnduranceEnragedPredictionSystem _enraged = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeAttackRateEvent>(OnMeleeAttackRate);
    }

    private void OnMeleeAttackRate(EntityUid uid, MeleeWeaponComponent component, ref GetMeleeAttackRateEvent args)
    {
        if (!TryComp<SpecialComponent>(args.User, out var special))
            return;

        args.Multipliers *= GetActionDelayMultiplier(args.User, special);
        if (HasComp<ItemComponent>(uid) && !HasComp<GunComponent>(uid))
            args.Multipliers *= _enraged.GetMeleeDelayMultiplier(args.User, component.AttackRate);
    }

    public float GetStaminaCostMultiplier(EntityUid uid)
    {
        var points = Math.Clamp(_special.GetEffective(uid, SpecialStat.Agility) - 5, 0, 5);
        return 1f - points * 0.04f;
    }

    private float GetActionDelayMultiplier(EntityUid uid, SpecialComponent special)
    {
        var points = Math.Clamp(_special.GetEffective(uid, SpecialStat.Agility) - 5, 0, 5);
        if (points > 0)
            return 1f / (1f + points * 0.04f);

        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            uid,
            SpecialStat.Agility,
            -tuning.AgilityActionDelayMultiplierPerPoint,
            special);

        return MathF.Max(0.1f, 1f + modifier);
    }
}
