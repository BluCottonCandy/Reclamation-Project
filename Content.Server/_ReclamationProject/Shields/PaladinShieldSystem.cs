using System.Linq;
using System.Numerics;
using Content.Server.Interaction;
using Content.Server.Hands.Systems;
using Content.Server.Weapons.Melee;
using Content.Shared._Misfits.MeleeCharge;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Alert;
using Content.Shared.Hands;
using Content.Shared._ReclamationProject.Shields;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Explosion;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Shields;

public sealed class PaladinShieldSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    private readonly HashSet<EntityUid> _replace = new();
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly PowerArmorIntegritySystem _integrity = default!;
    [Dependency] private readonly MeleeChargeSystem _dash = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly PaladinShieldProtectionSystem _protection = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ThrownItemSystem _thrown = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly MeleeWeaponSystem _melee = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PaladinShieldComponent, GotEquippedHandEvent>(OnHeld);
        SubscribeLocalEvent<PaladinShieldComponent, GotUnequippedHandEvent>(OnDropped);
        SubscribeLocalEvent<PaladinShieldComponent, DamageChangedEvent>(OnShieldDamage);
        SubscribeLocalEvent<PaladinShieldComponent, ShieldWearEvent>(OnWear);
        SubscribeLocalEvent<ShieldCarrierComponent, BeforeExplodeEvent>(OnBlast, after: new[] { typeof(HandsSystem) });
        SubscribeLocalEvent<PaladinShieldComponent, GetItemActionsEvent>(OnActions);
        SubscribeLocalEvent<PaladinShieldComponent, PaladinShieldChargeEvent>(OnCharge);
        SubscribeLocalEvent<HandsComponent, GetExplosionResistanceEvent>(OnExplosion);
        SubscribeLocalEvent<ShieldChargeComponent, ThrowDoHitEvent>(OnThrowHit);
        SubscribeLocalEvent<ShieldChargeComponent, StartCollideEvent>(OnCollide);
        SubscribeLocalEvent<ShieldChargeComponent, UpdateCanMoveEvent>(OnMove);
    }

    private void OnHeld(EntityUid uid, PaladinShieldComponent comp, GotEquippedHandEvent args)
    {
        EnsureComp<ShieldCarrierComponent>(args.User);
        _integrity.UpdateIntegrityAlert(args.User, uid, Comp<PowerArmorIntegrityComponent>(uid));
    }

    private void OnDropped(EntityUid uid, PaladinShieldComponent comp, GotUnequippedHandEvent args)
    {
        _alerts.ClearAlert(args.User, Comp<PowerArmorIntegrityComponent>(uid).IntegrityAlert);
        foreach (var held in _hands.EnumerateHeld(args.User))
        {
            if (held != uid && HasComp<PaladinShieldComponent>(held))
                _integrity.UpdateIntegrityAlert(args.User, held, Comp<PowerArmorIntegrityComponent>(held));
        }
    }

    private void OnWear(Entity<PaladinShieldComponent> shield, ref ShieldWearEvent args)
    {
        if (_protection.IsFunctional(shield, shield.Comp))
            _damage.TryChangeDamage(shield, args.Damage, ignoreResistances: true, interruptsDoAfters: false);
    }

    private void OnBlast(Entity<ShieldCarrierComponent> user, ref BeforeExplodeEvent args)
    {
        if (_protection.GetShield(user) is not { } shield)
            return;
        var comp = Comp<PaladinShieldComponent>(shield);
        // This shield already receives its share here; omit the normal held-item recursion.
        args.Contents.Remove(shield);
        if (_protection.IsRaised(shield))
        {
            _damage.TryChangeDamage(shield, new DamageSpecifier(args.Damage),
                ignoreResistances: true, interruptsDoAfters: false);
            foreach (var type in args.Damage.DamageDict.Keys.ToArray())
            {
                if (args.Damage.DamageDict[type] > 0)
                    args.Damage.DamageDict[type] = 0;
            }
            return;
        }
        // The explosion event already includes this shield's resistance.
        _damage.TryChangeDamage(shield, args.Damage * (comp.WearFraction / comp.DamageCoefficient),
            ignoreResistances: true, interruptsDoAfters: false);
    }

    private void OnShieldDamage(EntityUid uid, PaladinShieldComponent comp, DamageChangedEvent args)
    {
        var damage = Comp<DamageableComponent>(uid);
        var integrity = Comp<PowerArmorIntegrityComponent>(uid);
        if ((!comp.Damaged && damage.TotalDamage >= integrity.MaxIntegrity) || (comp.Damaged && damage.TotalDamage == 0))
            _replace.Add(uid);
        var parent = Transform(uid).ParentUid;
        if (HasComp<HandsComponent>(parent) && _hands.IsHolding(parent, uid, out _))
            _integrity.UpdateIntegrityAlert(parent, uid, integrity);
    }

    private void ReplaceShield(EntityUid uid)
    {
        if (!TryComp<PaladinShieldComponent>(uid, out var comp) || Deleted(uid))
            return;
        var parent = Transform(uid).ParentUid;
        _containers.TryGetContainingContainer((uid, null, null), out var originalContainer);
        string? handName = null;
        if (HasComp<HandsComponent>(parent) && _hands.IsHolding(parent, uid, out var hand))
        {
            handName = hand.Name;
            if (!_hands.TryDrop(parent, uid, checkActionBlocker: false))
                return;
        }
        var location = _transform.ToCoordinates(_transform.GetMapCoordinates(uid));
        if (handName == null && originalContainer != null)
            _containers.Remove(uid, originalContainer);
        var replacement = Spawn(comp.Damaged ? comp.RepairedPrototype : comp.BrokenPrototype,
            location);
        if (handName != null)
            _hands.TryPickup(parent, replacement, handName: handName, checkActionBlocker: false);
        else if (originalContainer != null)
            _containers.Insert(replacement, originalContainer);
        QueueDel(uid);
    }

    private void OnActions(EntityUid uid, PaladinShieldComponent comp, GetItemActionsEvent args)
    {
        if (args.InHands && !comp.Damaged)
            args.AddAction(ref comp.ChargeAction, "ReclamationActionShieldCharge");
    }

    private void OnExplosion(Entity<HandsComponent> user, ref GetExplosionResistanceEvent args)
    {
        // Raised shields absorb the blast in BeforeExplode so they still take durability damage.
        if (_protection.GetShield(user) is not { } shield || !_protection.IsRaised(shield))
            args.DamageCoefficient *= _protection.GetProtection(user);
    }

    private void OnCharge(EntityUid uid, PaladinShieldComponent comp, PaladinShieldChargeEvent args)
    {
        if (args.Handled || !TryCharge(args.Performer, uid, args.Target))
            return;
        args.Handled = true;
        _actions.StartUseDelay(args.Action);
    }

    public bool TryCharge(EntityUid user, EntityUid shield, EntityCoordinates target)
    {
        if (!_mob.IsAlive(user) || !TryComp<PaladinShieldComponent>(shield, out var shieldComp) || !_protection.IsFunctional(shield, shieldComp) ||
            !_hands.IsHolding(user, shield, out _) || !_blocker.CanMove(user) || _protection.IsRaised(shield) ||
            HasComp<ThrownItemComponent>(user) || HasComp<MeleeChargeComponent>(user) ||
            !TryComp<PhysicsComponent>(user, out var body))
            return false;
        var comp = EnsureComp<ShieldChargeComponent>(user);
        if (comp.Active || _timing.CurTime < comp.NextCharge)
            return false;
        var start = _transform.GetMapCoordinates(user);
        var aim = _transform.ToMapCoordinates(target);
        var offset = aim.Position - start.Position;
        if (start.MapId != aim.MapId || offset.LengthSquared() < 0.01f)
            return false;
        var distance = MathF.Min(offset.Length(), 3.5f);
        var direction = Vector2.Normalize(offset);
        aim = new MapCoordinates(start.Position + direction * distance, start.MapId);
        if (!_interaction.InRangeUnobstructed(start, aim, distance + 0.1f,
                predicate: entity => entity == user || entity == shield || _mob.IsAlive(entity)))
            return false;
        comp.Shield = shield;
        comp.Start = start;
        comp.Direction = direction;
        comp.ImpactApplied = false;
        comp.StopPending = false;
        _dash.PerformDash(user, _transform.ToCoordinates(aim), 10, 3.5f);
        if (!TryComp<ThrownItemComponent>(user, out var thrown))
        {
            RemCompDeferred<MeleeChargeComponent>(user);
            return false;
        }
        // Match the queen's contact fix: short compensated throws otherwise land before impact.
        thrown.LandTime = _timing.CurTime + TimeSpan.FromSeconds(distance / MathF.Max(body.LinearVelocity.Length(), 0.1f));
        Dirty(user, thrown);
        comp.Active = true;
        comp.NextCharge = _timing.CurTime + TimeSpan.FromSeconds(4);
        comp.End = _timing.CurTime + TimeSpan.FromSeconds(1);
        _blocker.UpdateCanMove(user);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        foreach (var uid in _replace)
            ReplaceShield(uid);
        _replace.Clear();
        var query = EntityQueryEnumerator<ShieldChargeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Active)
                continue;
            var position = _transform.GetMapCoordinates(uid);
            if (comp.StopPending || !_mob.IsAlive(uid) || !_hands.IsHolding(uid, comp.Shield, out _) ||
                _timing.CurTime >= comp.End || position.MapId != comp.Start.MapId ||
                Vector2.Distance(position.Position, comp.Start.Position) >= 3.5f ||
                !TryComp<PhysicsComponent>(uid, out var body) || body.LinearVelocity.LengthSquared() < 0.0025f)
                Stop(uid, comp);
        }
    }

    private void OnMove(EntityUid uid, ShieldChargeComponent comp, UpdateCanMoveEvent args)
    {
        if (comp.Active)
            args.Cancel();
    }

    private void OnThrowHit(EntityUid uid, ShieldChargeComponent comp, ThrowDoHitEvent args) => Impact(uid, comp, args.Target);

    private void OnCollide(Entity<ShieldChargeComponent> user, ref StartCollideEvent args)
    {
        if (args.OurFixture.Hard && args.OtherFixture.Hard)
            Impact(user, user.Comp, args.OtherEntity);
    }

    private void Impact(EntityUid user, ShieldChargeComponent comp, EntityUid target)
    {
        if (!comp.Active || comp.ImpactApplied || !_mob.IsAlive(user))
            return;
        comp.ImpactApplied = true;
        comp.StopPending = true;
        if (!_hands.IsHolding(user, comp.Shield, out _) || !_mob.IsAlive(target) ||
            !TryComp<PaladinShieldComponent>(comp.Shield, out var shield) ||
            !TryComp<MeleeWeaponComponent>(comp.Shield, out var weapon))
            return;
        var position = _transform.GetMapCoordinates(user);
        var aim = new MapCoordinates(position.Position + comp.Direction * weapon.Range * weapon.HeavyRangeModifier, position.MapId);
        foreach (var victim in _melee.GetWideSweepTargets(user, weapon, aim, candidate =>
                     _mob.IsAlive(candidate)))
        {
            _damage.TryChangeDamage(victim, new DamageSpecifier(_prototypes.Index<DamageTypePrototype>("Blunt"), shield.ChargeDamage), origin: user);
            if (HasComp<StaminaComponent>(victim))
                _stamina.TakeStaminaDamage(victim, shield.StaminaDamage, source: user, with: comp.Shield);
        }
    }

    private void Stop(EntityUid user, ShieldChargeComponent comp)
    {
        comp.Active = false;
        comp.StopPending = false;
        if (TryComp<PhysicsComponent>(user, out var body))
        {
            _physics.SetLinearVelocity(user, Vector2.Zero, body: body);
            if (TryComp<ThrownItemComponent>(user, out var thrown))
            {
                _thrown.LandComponent(user, thrown, body, false);
                _thrown.StopThrow(user, thrown);
            }
        }
        RemCompDeferred<MeleeChargeComponent>(user);
        _blocker.UpdateCanMove(user);
    }
}
