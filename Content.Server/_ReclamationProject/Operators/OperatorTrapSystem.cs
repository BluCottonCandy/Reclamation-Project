using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared._ReclamationProject.Operators;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Operators;

[RegisterComponent]
public sealed partial class OperatorTrapsComponent : Component
{
    public EntityUid? CaltropsAction;
    public EntityUid? ShotgunAction;
    public EntityUid? MineAction;
}

[RegisterComponent]
public sealed partial class OperatorCaltropsComponent : Component;

[RegisterComponent]
public sealed partial class OperatorShotgunTrapComponent : Component
{
    public TimeSpan NextScan;
}

[RegisterComponent]
public sealed partial class OperatorCaltropSlowdownComponent : Component
{
    public TimeSpan Ends;
    public float Multiplier;
}

public sealed class OperatorTrapSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OperatorTrapsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<OperatorTrapsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<OperatorTrapsComponent, OperatorThrowCaltropsEvent>(OnThrow);
        SubscribeLocalEvent<OperatorTrapsComponent, OperatorPlaceMineEvent>(OnPlaceMine);
        SubscribeLocalEvent<OperatorTrapsComponent, OperatorBuildShotgunEvent>(OnBuild);
        SubscribeLocalEvent<OperatorTrapsComponent, OperatorFinishShotgunEvent>(OnFinish);
        SubscribeLocalEvent<OperatorCaltropsComponent, StepTriggerAttemptEvent>(OnCaltropAttempt);
        SubscribeLocalEvent<OperatorCaltropsComponent, StepTriggeredOnEvent>(OnCaltrops);
        SubscribeLocalEvent<OperatorCaltropsComponent, StepTriggeredOffEvent>(OnLeaveGlass);
        SubscribeLocalEvent<OperatorCaltropSlowdownComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
    }

    private void OnStartup(EntityUid uid, OperatorTrapsComponent component, ComponentStartup args)
    {
        _actions.AddAction(uid, ref component.CaltropsAction, "ActionOperatorThrowCaltrops");
        _actions.AddAction(uid, ref component.ShotgunAction, "ActionOperatorBuildShotgun");
        _actions.AddAction(uid, ref component.MineAction, "ActionOperatorPlaceMine");
    }

    private void OnShutdown(EntityUid uid, OperatorTrapsComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.CaltropsAction);
        _actions.RemoveAction(uid, component.ShotgunAction);
        _actions.RemoveAction(uid, component.MineAction);
    }

    private void OnPlaceMine(Entity<OperatorTrapsComponent> ent, ref OperatorPlaceMineEvent args)
    {
        if (args.Handled || !_mob.IsAlive(ent.Owner))
            return;

        Spawn("OperatorConcussionMine", args.Target);
        args.Handled = true;
    }

    private void OnThrow(Entity<OperatorTrapsComponent> ent, ref OperatorThrowCaltropsEvent args)
    {
        if (args.Handled || !_mob.IsAlive(ent.Owner))
            return;

        Spawn("OperatorCaltrops", args.Target);
        args.Handled = true;
    }

    private void OnBuild(Entity<OperatorTrapsComponent> ent, ref OperatorBuildShotgunEvent args)
    {
        if (args.Handled || !_mob.IsAlive(ent.Owner))
            return;

        var preview = Spawn("OperatorShotgunPreview", args.Target);
        _transform.SetWorldRotation(preview, _transform.GetWorldRotation(ent.Owner));

        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent.Owner, 10f,
                new OperatorFinishShotgunEvent(), ent.Owner, target: preview)
            { BreakOnMove = true, BreakOnDamage = true, NeedHand = true }))
        {
            QueueDel(preview);
            return;
        }

        args.Handled = true;
    }

    private void OnFinish(Entity<OperatorTrapsComponent> ent, ref OperatorFinishShotgunEvent args)
    {
        if (args.Target is not {} preview || !TryComp<TransformComponent>(preview, out var transform))
            return;

        if (!args.Cancelled && _mob.IsAlive(ent.Owner))
        {
            var trap = Spawn("OperatorShotgunTrap", transform.Coordinates);
            _transform.SetWorldRotation(trap, _transform.GetWorldRotation(preview));
        }

        QueueDel(preview);
    }

    private void OnCaltropAttempt(Entity<OperatorCaltropsComponent> ent, ref StepTriggerAttemptEvent args)
        => args.Continue = _mob.IsAlive(args.Tripper) && HasComp<InventoryComponent>(args.Tripper);

    private void OnCaltrops(Entity<OperatorCaltropsComponent> ent, ref StepTriggeredOnEvent args)
    {
        if (!_mob.IsAlive(args.Tripper) || !HasComp<InventoryComponent>(args.Tripper))
            return;

        var shoes = _inventory.TryGetSlotEntity(args.Tripper, "shoes", out _);
        var slowdown = EnsureComp<OperatorCaltropSlowdownComponent>(args.Tripper);
        slowdown.Multiplier = shoes ? 0.9f : 0.8f;
        slowdown.Ends = _timing.CurTime + TimeSpan.FromSeconds(5);
        _movement.RefreshMovementSpeedModifiers(args.Tripper);

        if (shoes)
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict["Piercing"] = 5;
        _damage.TryChangeDamage(args.Tripper, damage, origin: ent.Owner);
        _chat.TryEmoteWithChat(args.Tripper, "Scream");
    }

    private void OnLeaveGlass(Entity<OperatorCaltropsComponent> ent, ref StepTriggeredOffEvent args)
    {
        if (TryComp<OperatorCaltropSlowdownComponent>(args.Tripper, out var slowdown))
            slowdown.Ends = _timing.CurTime + TimeSpan.FromSeconds(5);
    }

    private void OnSpeed(Entity<OperatorCaltropSlowdownComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
        => args.ModifySpeed(ent.Comp.Multiplier, ent.Comp.Multiplier);

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OperatorCaltropSlowdownComponent>();
        while (query.MoveNext(out var uid, out var slowdown))
        {
            if (_timing.CurTime < slowdown.Ends)
                continue;

            RemComp<OperatorCaltropSlowdownComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }

        var traps = EntityQueryEnumerator<OperatorShotgunTrapComponent, TransformComponent>();
        while (traps.MoveNext(out var uid, out var trap, out var transform))
        {
            if (_timing.CurTime < trap.NextScan)
                continue;

            trap.NextScan = _timing.CurTime + TimeSpan.FromSeconds(0.1);
            var location = _transform.GetMapCoordinates(uid);
            var forward = (_transform.GetWorldRotation(uid) - Angle.FromDegrees(90)).ToVec();
            foreach (var target in _lookup.GetEntitiesInRange<MobStateComponent>(transform.Coordinates, 2.2f))
            {
                if (!_mob.IsAlive(target.Owner))
                    continue;

                var offset = _transform.GetMapCoordinates(target.Owner).Position - location.Position;
                var distance = Vector2.Dot(forward, offset);
                if (distance <= 0 || distance > 2f ||
                    Math.Abs(forward.X * offset.Y - forward.Y * offset.X) > 0.5f ||
                    !_interaction.InRangeUnobstructed(uid, target.Owner, range: 2.1f))
                    continue;

                FireShotgun(uid);
                break;
            }
        }
    }

    private void FireShotgun(EntityUid uid)
    {
        var location = _transform.GetMapCoordinates(uid);
        var rotation = _transform.GetWorldRotation(uid);
        var forward = (rotation - Angle.FromDegrees(90)).ToVec();
        var origin = _transform.ToCoordinates(new MapCoordinates(location.Position + forward * 0.4f, location.MapId));
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/shotgun.ogg"), uid);
        for (var pellet = 0; pellet < 6; pellet++)
        {
            var projectile = Spawn("PelletShotgun12", origin);
            var direction = (rotation + Angle.FromDegrees(-97.5 + pellet * 3)).ToVec();
            _gun.ShootProjectile(projectile, direction, Vector2.Zero, uid, speed: 60f);
        }

        for (var plank = 0; plank < 2; plank++)
            Spawn("MaterialWoodPlank1", Transform(uid).Coordinates);
        for (var steel = 0; steel < 3; steel++)
            Spawn("SheetSteel1", Transform(uid).Coordinates);

        QueueDel(uid);
    }
}

