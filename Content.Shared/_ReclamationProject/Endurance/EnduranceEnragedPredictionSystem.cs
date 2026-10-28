using Content.Shared.Movement.Systems;
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
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<EnduranceEnragedComponent, RefreshMovementSpeedModifiersEvent>(OnMovement);
        SubscribeLocalEvent<EnduranceEnragedComponent, AfterAutoHandleStateEvent>(OnStateHandled);
        SubscribeLocalEvent<EnduranceEnragedComponent, ComponentShutdown>(OnShutdown);
        if (_net.IsServer)
            return;

        SubscribeLocalEvent<SpecialComponent, BeforeMobStateChangedEvent>(OnStateAttempt);
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
