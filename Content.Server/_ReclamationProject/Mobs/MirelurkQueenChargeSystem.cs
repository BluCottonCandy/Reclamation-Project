using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Content.Server.Interaction;
using Content.Server.Weapons.Melee;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Content.Shared.Weapons.Melee;
using Content.Shared.ActionBlocker;
using Content.Shared.Movement.Events;
using Robust.Shared.Physics.Events;
using Content.Server.NPC.Components;
using Content.Shared._Misfits.MeleeCharge;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Mobs;

public sealed class MirelurkQueenChargeSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly MeleeChargeSystem _charge = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ThrownItemSystem _thrown = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MeleeWeaponSystem _melee = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MirelurkQueenChargeComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MirelurkQueenChargeComponent, ThrowDoHitEvent>(OnImpact);
        SubscribeLocalEvent<MirelurkQueenChargeComponent, StartCollideEvent>(OnBodyImpact);
        SubscribeLocalEvent<MirelurkQueenChargeComponent, UpdateCanMoveEvent>(OnMoveAttempt);
    }

    private void OnDamageChanged(Entity<MirelurkQueenChargeComponent> queen, ref DamageChangedEvent args)
    {
        if (queen.Comp.Enraged || !_mobState.IsAlive(queen) ||
            !TryComp<DamageableComponent>(queen, out var damage) || damage.TotalDamage < queen.Comp.EnrageDamageThreshold)
            return;

        // Healing never resets or repeats this transition.
        queen.Comp.Enraged = true;
        queen.Comp.NextCharge = _timing.CurTime;
        queen.Comp.StaminaDamage *= 1.2f;
        if (queen.Comp.ImpactDamage != null)
            queen.Comp.ImpactDamage *= 1.2f;
        if (TryComp<PassiveDamageComponent>(queen, out var regeneration))
        {
            regeneration.Interval /= 1.4f;
            Dirty(queen, regeneration);
        }
        if (TryComp<MeleeWeaponComponent>(queen, out var weapon))
        {
            weapon.Damage *= 1.2f;
            Dirty(queen, weapon);
        }
        if (queen.Comp.WindingUp)
            queen.Comp.WindupEnd = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MirelurkQueenChargeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.StopPending || (!_mobState.IsAlive(uid) && (comp.Dashing || comp.WindingUp)))
                StopCharge(uid, comp);

            if (comp.WindingUp)
            {
                if (!_mobState.IsAlive(uid) || !_mobState.IsAlive(comp.Target) ||
                    !HasComp<ActiveNPCComponent>(uid) || HasComp<ActorComponent>(uid))
                {
                    StopCharge(uid, comp);
                    continue;
                }
                if (TryComp<PhysicsComponent>(uid, out var body))
                    _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
                if (_timing.CurTime >= comp.WindupEnd)
                    FinishWindup((uid, comp));
                continue;
            }

            if (comp.Dashing)
            {
                // A compensated throw lands before its forward slide is finished.
                // Keep its collision damage and movement lock until it actually stops.
                var position = _transform.GetMapCoordinates(uid);
                if (position.MapId != comp.DashStart.MapId || Vector2.Distance(position.Position, comp.DashStart.Position) >= comp.Range ||
                    _timing.CurTime >= comp.DashEnd || !TryComp<PhysicsComponent>(uid, out var dashBody) ||
                    dashBody.LinearVelocity.LengthSquared() < 0.0025f)
                    StopCharge(uid, comp);
                continue;
            }

            if (!HasComp<ActiveNPCComponent>(uid) || HasComp<ActorComponent>(uid) ||
                !TryComp<NPCMeleeCombatComponent>(uid, out var combat))
                continue;

            TryCharge((uid, comp), combat.Target);
        }
    }

    public bool TryCharge(Entity<MirelurkQueenChargeComponent> queen, EntityUid target)
    {
        var comp = queen.Comp;
        if (comp.WindingUp || comp.Dashing || _timing.CurTime < comp.NextCharge || !_mobState.IsAlive(queen) || !_mobState.IsAlive(target) ||
            HasComp<MeleeChargeComponent>(queen) || HasComp<ThrownItemComponent>(queen) ||
            !TryComp<TransformComponent>(queen, out var queenXform) ||
            !TryComp<TransformComponent>(target, out var targetXform) || !HasComp<PhysicsComponent>(queen))
            return false;

        var start = _transform.GetMapCoordinates(queen, queenXform);
        var end = _transform.GetMapCoordinates(target, targetXform);
        if (start.MapId != end.MapId)
            return false;
        var distance = Vector2.Distance(start.Position, end.Position);
        if (distance < comp.MinimumRange || distance > comp.Range ||
            !_interaction.InRangeUnobstructed(start, end, comp.Range, predicate: entity => entity == queen.Owner || entity == target))
            return false;

        comp.NextCharge = _timing.CurTime + TimeSpan.FromSeconds(comp.Enraged ? comp.Cooldown / 2 : comp.Cooldown);
        comp.WindupEnd = _timing.CurTime + TimeSpan.FromSeconds(comp.Enraged ? 0 : comp.Windup);
        comp.WindingUp = true;
        comp.Dashing = false;
        comp.Target = target;
        comp.TargetPosition = end;
        comp.ImpactApplied = false;
        comp.StopPending = false;
        // The existing dash marker blocks movement during the tell as well as the dash.
        EnsureComp<MeleeChargeComponent>(queen);
        _physics.SetLinearVelocity(queen, Vector2.Zero);
        if (TryComp<MeleeWeaponComponent>(queen, out var weapon) && weapon.NextAttack < comp.WindupEnd)
        {
            weapon.NextAttack = comp.WindupEnd;
            Dirty(queen, weapon);
        }
        if (TryComp<MeleeChargeAudioComponent>(queen, out var audio))
            _audio.PlayPvs(audio.ActivationSound, queen);
        if (comp.Enraged)
            FinishWindup(queen);
        return true;
    }

    private void FinishWindup(Entity<MirelurkQueenChargeComponent> queen)
    {
        var comp = queen.Comp;
        var start = _transform.GetMapCoordinates(queen);
        if (start.MapId != comp.TargetPosition.MapId ||
            !_interaction.InRangeUnobstructed(start, comp.TargetPosition, comp.Range,
                predicate: entity => entity == queen.Owner || entity == comp.Target))
        {
            StopCharge(queen, comp);
            return;
        }

        comp.WindingUp = false;
        comp.DashStart = start;
        comp.DashDirection = (comp.TargetPosition.Position - start.Position).Normalized();
        // Use the same stamina-free dash movement, aimed at the windup's original position.
        _charge.PerformDash(queen, _transform.ToCoordinates(comp.TargetPosition), comp.Speed, comp.Range);
        comp.Dashing = TryComp<ThrownItemComponent>(queen, out var activeThrow);
        if (comp.Dashing && TryComp<PhysicsComponent>(queen, out var body))
        {
            // A short compensated throw can otherwise land immediately, before body contact.
            // Keep the existing throw airborne until it reaches the aimed position.
            var travel = Vector2.Distance(start.Position, comp.TargetPosition.Position);
            var duration = travel / MathF.Max(body.LinearVelocity.Length(), 0.1f);
            activeThrow!.LandTime = _timing.CurTime + TimeSpan.FromSeconds(duration);
            Dirty(queen, activeThrow);
        }
        comp.DashEnd = _timing.CurTime + TimeSpan.FromSeconds(1);
        _blocker.UpdateCanMove(queen);
        if (!comp.Dashing)
            StopCharge(queen, comp);
    }

    private void OnMoveAttempt(EntityUid uid, MirelurkQueenChargeComponent comp, UpdateCanMoveEvent args)
    {
        if (comp.WindingUp || comp.Dashing)
            args.Cancel();
    }

    private void OnBodyImpact(Entity<MirelurkQueenChargeComponent> queen, ref StartCollideEvent args)
    {
        if (args.OurFixture.Hard && args.OtherFixture.Hard)
            ApplyImpact(queen, args.OtherEntity);
    }

    private void OnImpact(EntityUid uid, MirelurkQueenChargeComponent component, ThrowDoHitEvent args)
    {
        ApplyImpact((uid, component), args.Target);
    }

    private void ApplyImpact(Entity<MirelurkQueenChargeComponent> queen, EntityUid target)
    {
        if (!queen.Comp.Dashing || queen.Comp.WindingUp || queen.Comp.ImpactApplied || !_mobState.IsAlive(queen))
            return;

        // Throw collision events can repeat for different fixtures. Only the first
        // impact of this charge counts; walls stop it without inflicting stamina damage.
        queen.Comp.ImpactApplied = true;
        queen.Comp.StopPending = true;
        if (!_mobState.IsAlive(target) || !HasComp<StaminaComponent>(target) ||
            _factions.IsEntityFriendly(queen.Owner, target) || !TryComp<MeleeWeaponComponent>(queen, out var weapon))
            return;

        var position = _transform.GetMapCoordinates(queen);
        var direction = queen.Comp.DashDirection;
        if (direction.LengthSquared() < 0.001f)
            direction = (queen.Comp.TargetPosition.Position - position.Position).Normalized();
        var aim = new MapCoordinates(position.Position + direction * weapon.Range * weapon.HeavyRangeModifier, position.MapId);
        var victims = _melee.GetWideSweepTargets(queen, weapon, aim, candidate =>
            _mobState.IsAlive(candidate) && HasComp<StaminaComponent>(candidate) && !_factions.IsEntityFriendly(queen.Owner, candidate));
        foreach (var victim in victims)
        {
            if (queen.Comp.StaminaDamage > 0)
                _stamina.TakeStaminaDamage(victim, queen.Comp.StaminaDamage, source: queen, with: queen);
            if (queen.Comp.ImpactDamage != null)
                _damage.TryChangeDamage(victim, queen.Comp.ImpactDamage, origin: queen);
            else if (queen.Comp.Enraged)
                _damage.TryChangeDamage(victim, new DamageSpecifier(_prototypes.Index<DamageTypePrototype>("Blunt"), 30), origin: queen);
        }

        var xform = Transform(queen);
        var localAim = Vector2.Transform(aim.Position, _transform.GetInvWorldMatrix(xform));
        localAim = xform.LocalRotation.RotateVec(localAim).Normalized() * (weapon.Range * weapon.HeavyRangeModifier - 0.2f);
        _melee.DoLunge(queen, queen, weapon.Angle, localAim, weapon.WideAnimation, predicted: false);
    }

    private void StopCharge(EntityUid queen, MirelurkQueenChargeComponent comp)
    {
        comp.StopPending = false;
        comp.WindingUp = false;
        comp.Dashing = false;
        if (TryComp<PhysicsComponent>(queen, out var physics))
        {
            _physics.SetLinearVelocity(queen, Vector2.Zero, body: physics);
            if (TryComp<ThrownItemComponent>(queen, out var thrown))
            {
                _thrown.LandComponent(queen, thrown, physics, false);
                _thrown.StopThrow(queen, thrown);
            }
        }
        RemCompDeferred<MeleeChargeComponent>(queen);
        _blocker.UpdateCanMove(queen);
    }
}
