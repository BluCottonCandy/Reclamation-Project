using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffect;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameStates;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._ReclamationProject.Endurance;

/// <summary>Keeps predicted health/mob states consistent with the server's protection.</summary>
public sealed class EnduranceEnragedPredictionSystem : EntitySystem
{
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<EnduranceEnragedComponent, RefreshMovementSpeedModifiersEvent>(OnMovement);
        SubscribeLocalEvent<EnduranceEnragedComponent, AfterAutoHandleStateEvent>(OnStateHandled);
        SubscribeLocalEvent<EnduranceEnragedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<EnduranceEnragedComponent, BeforeStatusEffectAddedEvent>(OnStatusEffect);
        if (_net.IsServer)
            return;

        SubscribeLocalEvent<SpecialComponent, BeforeMobStateChangedEvent>(OnStateAttempt);
    }

    private void OnStatusEffect(EntityUid uid, EnduranceEnragedComponent comp, ref BeforeStatusEffectAddedEvent args)
    {
        if (comp.Active && _timing.CurTime < comp.EndsAt && args.Key is "Stun" or "KnockedDown")
            args.Cancelled = true;
    }

    // AttackRate is the base delay in seconds: the super sledge is 1.35, the rotor blade 1.53.
    public float GetMeleeDelayMultiplier(EntityUid user, float baseDelay)
    {
        if (baseDelay < 1.35f || !TryComp<EnduranceEnragedComponent>(user, out var rage) ||
            !rage.Active || _timing.CurTime >= rage.EndsAt)
            return 1f;

        return 1f / 1.5f;
    }

    private void OnMovement(EntityUid uid, EnduranceEnragedComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (comp.Active && _timing.CurTime < comp.EndsAt)
            args.IgnoreSlowdowns = true;
    }

    private void OnStateHandled(EntityUid uid, EnduranceEnragedComponent comp, ref AfterAutoHandleStateEvent args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnShutdown(EntityUid uid, EnduranceEnragedComponent comp, ComponentShutdown args)
    {
        comp.Active = false;
        if (_net.IsServer && comp.PreviousStaminaThreshold is { } threshold)
        {
            comp.PreviousStaminaThreshold = null;
            if (!TerminatingOrDeleted(uid))
                _stamina.SetCritThreshold(uid, threshold);
        }
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnStateAttempt(EntityUid uid, SpecialComponent comp, ref BeforeMobStateChangedEvent args)
    {
        // The server owns activation and cooldown. Predict only the protection,
        // including the initial hit before its authoritative component arrives.
        if (_net.IsServer || args.Cancelled || args.OldState != MobState.Alive ||
            args.NewState is not (MobState.SoftCritical or MobState.Critical or MobState.Dead))
            return;

        var rage = CompOrNull<EnduranceEnragedComponent>(uid);
        if (rage is { Active: true } && _timing.CurTime < rage.EndsAt ||
            _special.GetEffective(uid, SpecialStat.Endurance, comp) >= 10 &&
            (rage == null || _timing.CurTime >= rage.ReadyAt))
            args.Cancelled = true;
    }
}
