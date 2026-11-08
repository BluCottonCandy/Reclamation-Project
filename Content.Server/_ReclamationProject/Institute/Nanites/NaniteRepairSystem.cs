using System.Linq;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._ReclamationProject.Institute.Nanites;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Institute.Nanites;

/// <summary>
/// Waits until the body has gone <see cref="NaniteRepairComponent.Delay"/> without being hurt, then
/// repairs a little of everything once a second until there's nothing left to fix.
/// </summary>
public sealed class NaniteRepairSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly TraumaSystem _trauma = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NaniteRepairComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NaniteRepairComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnMapInit(Entity<NaniteRepairComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.LastDamaged = _timing.CurTime;
        ent.Comp.NextUpdate = _timing.CurTime + UpdateInterval;
    }

    private void OnDamaged(Entity<NaniteRepairComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        var hurt = args.DamageDelta.DamageDict.Any(entry =>
            entry.Value > 0 && !ent.Comp.IgnoredDamageTypes.Contains(entry.Key));
        if (!hurt)
            return;

        // Getting hurt stops the repair and starts the wait again.
        ent.Comp.LastDamaged = _timing.CurTime;
        ent.Comp.Repairing = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<NaniteRepairComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var nanites, out var damageable))
        {
            if (now < nanites.NextUpdate)
                continue;

            var seconds = (float) (now - nanites.NextUpdate + UpdateInterval).TotalSeconds;
            nanites.NextUpdate = now + UpdateInterval;

            if (_mobState.IsDead(uid) || now - nanites.LastDamaged < nanites.Delay)
                continue;

            var repaired = Repair(uid, nanites, damageable, seconds);
            if (repaired && !nanites.Repairing)
            {
                nanites.Repairing = true;
                _popup.PopupEntity(Loc.GetString(nanites.StartMessage), uid, uid, PopupType.Medium);
            }
            else if (!repaired && nanites.Repairing)
            {
                nanites.Repairing = false;
                _popup.PopupEntity(Loc.GetString(nanites.FinishMessage), uid, uid, PopupType.Medium);
            }
        }
    }

    /// <summary>Repairs one tick's worth of everything. Returns false once there's nothing left to fix.</summary>
    private bool Repair(EntityUid uid, NaniteRepairComponent nanites, DamageableComponent damageable, float seconds)
    {
        var amount = nanites.HealPerSecond * seconds;
        var repaired = false;

        if (HealingFor(damageable, amount) is { } bodyHeal)
        {
            _damageable.TryChangeDamage(uid, bodyHeal, ignoreResistances: true, interruptsDoAfters: false,
                damageable: damageable, doPartDamage: false);
            repaired = true;
        }

        TryComp<TraumaBodyComponent>(uid, out var trauma);
        foreach (var (part, _) in _body.GetBodyChildren(uid).ToArray())
        {
            if (TryComp<DamageableComponent>(part, out var partDamage) && HealingFor(partDamage, amount) is { } partHeal)
            {
                // Trauma throws away ordinary healing on limbs, so go through its own path.
                if (HasComp<TraumaLimbComponent>(part))
                    _trauma.HealPart(part, partHeal);
                else
                    _damageable.TryChangeDamage(part, partHeal, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
                repaired = true;
            }

            // Failed limbs get their lost maximum health back (the failure stays on record).
            if (trauma != null && _trauma.RestoreLostCapacity(part, nanites.CapacityPerSecond * seconds))
                repaired = true;

            if (trauma != null && TryComp<FractureComponent>(part, out var fracture))
            {
                fracture.HealingProgress = Math.Min(100f, fracture.HealingProgress + nanites.BonePerSecond * seconds);
                Dirty(part, fracture);
                if (fracture.HealingProgress >= 100f)
                    _trauma.Repair(uid, part, TraumaKind.Fracture);
                repaired = true;
            }
        }

        if (trauma != null)
        {
            foreach (var (organ, _) in _body.GetBodyOrgans(uid).ToArray())
            {
                if (!TryComp<TraumaOrganComponent>(organ, out var injury))
                    continue;
                if (_trauma.RestoreLostCapacity(organ, nanites.CapacityPerSecond * seconds))
                    repaired = true;
                if (injury.Damage <= 0f)
                    continue;
                _trauma.SetOrganDamage(organ, Math.Max(0f, injury.Damage - nanites.OrganPerSecond * seconds));
                repaired = true;
            }

            if (trauma.InternalBleeding > 0f)
            {
                trauma.InternalBleeding = Math.Max(0f, trauma.InternalBleeding - 0.5f * seconds);
                Dirty(uid, trauma);
                repaired = true;
            }
        }

        if (TryComp<BloodstreamComponent>(uid, out var blood))
        {
            if (blood.BleedAmount > 0f)
            {
                _bloodstream.TryModifyBleedAmount(uid, -blood.BleedAmount, blood);
                repaired = true;
            }

            if (_bloodstream.GetBloodLevelPercentage(uid, blood) < 1f)
            {
                _bloodstream.TryModifyBloodLevel(uid, FixedPoint2.New(nanites.BloodPerSecond * seconds), blood, createPuddle: false);
                repaired = true;
            }
        }

        return repaired;
    }

    /// <summary>Up to <paramref name="amount"/> of healing for each damage type that has any damage.</summary>
    private static DamageSpecifier? HealingFor(DamageableComponent damageable, float amount)
    {
        DamageSpecifier? heal = null;
        foreach (var (type, value) in damageable.Damage.DamageDict)
        {
            if (value <= 0)
                continue;
            heal ??= new DamageSpecifier();
            heal.DamageDict[type] = -FixedPoint2.Min(value, FixedPoint2.New(amount));
        }

        return heal;
    }
}
