using Content.Shared.Movement.Systems;
using Content.Shared._ReclamationProject.Endurance;
using Content.Server.Nutrition;
using Content.Shared.Chemistry;
using Content.Shared.Body.Part;
using Content.Shared.Body.Organ;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Server.Popups;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Endurance;

/// <summary>Endurance 10 delays incapacitation, without healing or discarding accumulated damage.</summary>
public sealed class EnduranceEnragedSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpecialComponent, BeforeMobStateChangedEvent>(OnStateAttempt);
        SubscribeLocalEvent<EnduranceEnragedComponent, BeforeDamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<BodyPartComponent, BeforeDamageChangedEvent>(OnPartDamage);
        SubscribeLocalEvent<OrganComponent, BeforeDamageChangedEvent>(OnOrganDamage);
        SubscribeLocalEvent<EnduranceEnragedComponent, MedicalTreatmentAttemptEvent>(OnTreatment);
        SubscribeLocalEvent<EnduranceEnragedComponent, IngestionAttemptEvent>(OnIngest);
    }

    private void OnStateAttempt(EntityUid uid, SpecialComponent comp, ref BeforeMobStateChangedEvent args)
    {
        if (args.Cancelled || args.OldState != MobState.Alive ||
            args.NewState is not (MobState.SoftCritical or MobState.Critical or MobState.Dead))
            return;

        var rage = CompOrNull<EnduranceEnragedComponent>(uid);
        var now = _timing.CurTime;
        if (rage == null || !rage.Active)
        {
            if (_special.GetEffective(uid, SpecialStat.Endurance, comp) < 10 ||
                rage != null && now < rage.ReadyAt)
                return;

            rage ??= EnsureComp<EnduranceEnragedComponent>(uid);
            rage.Active = true;
            rage.EndsAt = now + TimeSpan.FromSeconds(10);
            rage.ReadyAt = now + TimeSpan.FromMinutes(5);
            rage.ForcedState = null;
            Dirty(uid, rage);
            _movement.RefreshMovementSpeedModifiers(uid);
            _popup.PopupEntity(Loc.GetString("endurance-enraged-start"), uid, uid);
        }

        // Preserve explicit state changes (e.g. organ failure), while threshold-driven
        // changes are recalculated from accumulated damage when Enraged expires.
        if (!args.FromUpdate && (rage.ForcedState == null || args.NewState == MobState.Dead || rage.ForcedState == MobState.SoftCritical))
            rage.ForcedState = args.NewState;
        args.Cancelled = true;
    }

    private bool IsEnraged(EntityUid uid)
    {
        return TryComp<EnduranceEnragedComponent>(uid, out var rage) &&
            rage.Active && _timing.CurTime < rage.EndsAt;
    }

    private void OnTreatment(EntityUid uid, EnduranceEnragedComponent comp, ref MedicalTreatmentAttemptEvent args)
    {
        if (IsEnraged(uid))
            args.Cancelled = true;
    }

    private void OnIngest(EntityUid uid, EnduranceEnragedComponent comp, IngestionAttemptEvent args)
    {
        if (IsEnraged(uid))
            args.Cancel();
    }

    private void OnPartDamage(EntityUid uid, BodyPartComponent comp, ref BeforeDamageChangedEvent args)
    {
        if (comp.Body is { } body && IsEnraged(body))
            BlockHealing(ref args);
    }

    private void OnOrganDamage(EntityUid uid, OrganComponent comp, ref BeforeDamageChangedEvent args)
    {
        if (comp.Body is { } body && IsEnraged(body))
            BlockHealing(ref args);
    }

    private static void BlockHealing(ref BeforeDamageChangedEvent args)
    {
        var damage = new DamageSpecifier(args.Damage);
        foreach (var (type, value) in args.Damage.DamageDict)
        {
            if (value < 0)
                damage.DamageDict[type] = 0;
        }
        args.Damage = damage;
    }

    private void OnDamage(EntityUid uid, EnduranceEnragedComponent comp, ref BeforeDamageChangedEvent args)
    {
        if (!IsEnraged(uid))
            return;

        BlockHealing(ref args);
        var damage = new DamageSpecifier(args.Damage);
        foreach (var (type, value) in args.Damage.DamageDict)
        {
            if (value > 0)
                damage.DamageDict[type] = value * 1.25f;
        }
        args.Damage = damage;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<EnduranceEnragedComponent>();
        while (query.MoveNext(out var uid, out var rage))
        {
            if (!rage.Active || _timing.CurTime < rage.EndsAt)
                continue;

            rage.Active = false;
            Dirty(uid, rage);
            _movement.RefreshMovementSpeedModifiers(uid);
            _popup.PopupEntity(Loc.GetString("endurance-enraged-end"), uid, uid);
            // Recheck actual accumulated damage; CurrentThresholdState stayed alive
            // during protection and must not be reused as the final injury state.
            _thresholds.VerifyThresholds(uid);
            if (rage.ForcedState is { } state &&
                (state == MobState.Dead || _mobState.IsAlive(uid) ||
                 state == MobState.Critical && _mobState.IsSoftCritical(uid)))
                _mobState.ChangeMobState(uid, state);
            rage.ForcedState = null;
        }
    }
}
