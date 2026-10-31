using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Movement.Events;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;


namespace Content.Shared._Misfits.MeleeCharge;


public sealed class MeleeChargeSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeChargeComponent, UpdateCanMoveEvent>(OnMoveAttempt);
        SubscribeLocalEvent<MeleeChargeComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<MeleeChargeComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MeleeChargeComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<MeleeChargeComponent, StopThrowEvent>(OnStopThrow);
        SubscribeLocalEvent<MeleeChargeEvent>(OnDash);
    }

    private void OnDash(MeleeChargeEvent ev)
    {
        if (ev.Handled || HasComp<MeleeChargeComponent>(ev.Performer))
            return;

        if (TryComp<MeleeChargeAudioComponent>(ev.Performer, out var audio))
            _audio.PlayPredicted(audio.ActivationSound, ev.Performer, ev.Performer);

        PerformDash(ev.Performer, ev.Target, ev.Speed, ev.Range);
        ev.Handled = HasComp<ThrownItemComponent>(ev.Performer);
    }

    private void OnShutdown(Entity<MeleeChargeComponent> ent, ref ComponentShutdown args)
    {
        _blocker.UpdateCanMove(ent);
    }

    private void OnInit(Entity<MeleeChargeComponent> ent, ref ComponentInit args)
    {
        _blocker.UpdateCanMove(ent);
    }

    private void OnLand(Entity<MeleeChargeComponent> ent, ref LandEvent args)
    {
        if (TryComp<MeleeChargeAudioComponent>(ent, out var audio))
            _audio.PlayPredicted(audio.ImpactSound, ent, args.User ?? ent.Owner);

        RemCompDeferred<MeleeChargeComponent>(ent);
    }

    private void OnStopThrow(Entity<MeleeChargeComponent> ent, ref StopThrowEvent args)
    {
        // Also release the lock when interrupted or landing in zero gravity.
        RemCompDeferred<MeleeChargeComponent>(ent);
    }

    private void OnMoveAttempt(Entity<MeleeChargeComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        //Cant move while dashing
        args.Cancel();
    }

    public void PerformDash(EntityUid ent, EntityCoordinates targetPosition, float speed = 10f, float maxDistance = 3.5f)
    {
        if (HasComp<MeleeChargeComponent>(ent) || HasComp<ThrownItemComponent>(ent) ||
            !TryComp<PhysicsComponent>(ent, out var body))
            return;

        var entMapPos = _transform.ToMapCoordinates(Transform(ent).Coordinates);
        var targetMapPos = _transform.ToMapCoordinates(targetPosition);

        if (targetMapPos.MapId != entMapPos.MapId)
            return;

        var distance = Vector2.Distance(entMapPos.Position, targetMapPos.Position);

        if (distance > maxDistance)
        {
            var direction = (targetMapPos.Position - entMapPos.Position).Normalized();
            var clampedTarget = entMapPos.Position + direction * maxDistance;
            targetMapPos = new MapCoordinates(clampedTarget, entMapPos.MapId);
        }


        if (distance <= 0.01f || speed <= 0 || maxDistance <= 0)
            return;

        EnsureComp<MeleeChargeComponent>(ent);
        // A charge has its own velocity. Adding an impulse to walking/running momentum makes
        // its trajectory depend on the arrival time of movement input on the two peers.
        _physics.SetLinearVelocity(ent, Vector2.Zero, body: body);
        var finalTarget = _transform.ToCoordinates(targetMapPos);

        _throwing.TryThrow(ent, finalTarget, speed, null, 0f, 10, true, false, false, false, false);
        if (!HasComp<ThrownItemComponent>(ent))
            RemCompDeferred<MeleeChargeComponent>(ent);
    }

}
