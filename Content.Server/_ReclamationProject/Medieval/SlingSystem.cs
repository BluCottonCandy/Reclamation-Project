// #Reclamation Project - medieval weapons: the shepherd's sling.
// Loads any small-enough item, launches it with the gun system (it flies as a thrown item),
// adds bonus damage when it hits someone, and primes grenades as they leave the pouch.
using Content.Server.Explosion.EntitySystems;
using Content.Server.Popups;
using Content.Shared._ReclamationProject.Medieval;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Explosion.Components;
using Content.Shared.Item;
using Content.Shared.Throwing;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using System.Numerics;

namespace Content.Server._ReclamationProject.Medieval;

public sealed class SlingSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlingComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<SlingComponent, GunShotEvent>(OnShot);
        SubscribeLocalEvent<SlingProjectileComponent, ThrowDoHitEvent>(OnHit);
        SubscribeLocalEvent<SlingProjectileComponent, StopThrowEvent>(OnStop);
    }

    private void OnInsertAttempt(EntityUid uid, SlingComponent comp, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Cancelled || args.SlotEntity != uid)
            return;

        if (!TryComp<ItemComponent>(args.Item, out var item)
            || _item.GetSizePrototype(item.Size) > _item.GetSizePrototype(comp.MaxSize))
        {
            args.Cancelled = true;
            if (args.User is { } user)
                _popup.PopupEntity(Loc.GetString("rp-sling-too-big", ("item", args.Item)), uid, user);
        }
    }

    private void OnShot(EntityUid uid, SlingComponent comp, ref GunShotEvent args)
    {
        var shooter = args.User;
        Vector2? firstDir = null;
        if (TryComp<GunComponent>(uid, out var aimGun) && aimGun.ShootCoordinates is { } aimAt)
        {
            var d = _xform.ToMapCoordinates(aimAt).Position - _xform.GetMapCoordinates(shooter).Position;
            if (d.LengthSquared() >= 0.01f)
                firstDir = Vector2.Normalize(d);
        }
        foreach (var (ent, _) in args.Ammo)
        {
            if (ent is not { } e || TerminatingOrDeleted(e))
                continue;
            Arm(e, comp, shooter);
            // an ammo box or magazine that went out first: every round in it goes off too
            if (comp.FireCartridges && firstDir is { } fd)
                FireRoundsInside(uid, comp, e, fd, shooter);
        }

        // a sling lets go of everything in the pouch at once: throw (or fire) the rest along the same line, fanned out
        if (!TryComp<GunComponent>(uid, out var gun) || gun.ShootCoordinates is not { } target
            || !_container.TryGetContainer(uid, comp.Slot, out var pouch) || pouch.ContainedEntities.Count == 0)
            return;
        var from = _xform.GetMapCoordinates(shooter).Position;
        var to = _xform.ToMapCoordinates(target).Position;
        var aim = to - from;
        if (aim.LengthSquared() < 0.01f)
            return;
        var dir = Vector2.Normalize(aim);
        var speed = gun.ProjectileSpeedModified;
        var rest = new List<EntityUid>(pouch.ContainedEntities);
        foreach (var ent in rest)
        {
            if (!_container.Remove(ent, pouch))
                continue;
            _xform.SetCoordinates(ent, Transform(shooter).Coordinates);
            var angle = new Robust.Shared.Maths.Angle(_random.NextFloat(-comp.Spread, comp.Spread) * MathF.PI / 180f);
            if (comp.FireCartridges && TryComp<CartridgeAmmoComponent>(ent, out var cart))
            {
                FireCartridge(uid, comp, ent, cart, angle.RotateVec(dir), shooter);
                continue;
            }
            // ammo boxes and magazines: fire every round inside, then the empty box is hurled too
            if (comp.FireCartridges)
                FireRoundsInside(uid, comp, ent, dir, shooter);
            Arm(ent, comp, shooter);
            _throwing.TryThrow(ent, angle.RotateVec(dir) * 8f, speed, shooter, recoil: false);
        }
    }

    /// <summary>A bullet or shell in a Balearic sling volley fires like it came out of a gun.</summary>
    private void FireCartridge(EntityUid sling, SlingComponent comp, EntityUid ent, CartridgeAmmoComponent cart, Vector2 dir, EntityUid shooter)
    {
        if (cart.Spent)
        {
            _throwing.TryThrow(ent, dir * 4f, 8f, shooter, recoil: false);   // empty casings just fall out
            return;
        }
        var bullet = Spawn(cart.Prototype, Transform(shooter).Coordinates);
        _gun.ShootProjectile(bullet, dir, Vector2.Zero, sling, shooter, comp.BulletSpeed);
        // shotgun shells: the spawned shot carries ProjectileSpread with its extra pellets
        if (TryComp<ProjectileSpreadComponent>(bullet, out var spread))
        {
            for (var i = 1; i < spread.Count; i++)
            {
                var half = (float) spread.Spread.Theta / 2f;
                var pellet = Spawn(spread.Proto, Transform(shooter).Coordinates);
                _gun.ShootProjectile(pellet, new Robust.Shared.Maths.Angle(_random.NextFloat(-half, half)).RotateVec(dir),
                    Vector2.Zero, sling, shooter, comp.BulletSpeed);
            }
        }
        if (cart.DeleteOnSpawn)
        {
            QueueDel(ent);
            return;
        }
        // mark it spent the same way a gun does, so the casing also LOOKS empty on the floor
        cart.Spent = true;
        Dirty(ent, cart);
        _appearance.SetData(ent, AmmoVisuals.Spent, true);
        _throwing.TryThrow(ent, dir * 2f, 6f, shooter, recoil: false);
    }

    /// <summary>Empties an ammo box or magazine and fires every round in it, all at once.</summary>
    private void FireRoundsInside(EntityUid sling, SlingComponent comp, EntityUid box, Vector2 dir, EntityUid shooter)
    {
        if (!HasComp<BallisticAmmoProviderComponent>(box))
            return;
        var count = new GetAmmoCountEvent();
        RaiseLocalEvent(box, ref count);
        if (count.Count <= 0)
            return;
        var rounds = new List<(EntityUid? Entity, IShootable Shootable)>();
        RaiseLocalEvent(box, new TakeAmmoEvent(count.Count, rounds, Transform(shooter).Coordinates, shooter));
        foreach (var (round, _) in rounds)
        {
            if (round is not { } r || TerminatingOrDeleted(r))
                continue;
            _container.TryRemoveFromContainer(r);
            _xform.SetCoordinates(r, Transform(shooter).Coordinates);
            var angle = new Robust.Shared.Maths.Angle(_random.NextFloat(-comp.Spread, comp.Spread) * MathF.PI / 180f);
            if (TryComp<CartridgeAmmoComponent>(r, out var cart))
                FireCartridge(sling, comp, r, cart, angle.RotateVec(dir), shooter);
        }
    }

    private void Arm(EntityUid shot, SlingComponent comp, EntityUid shooter)
    {
        var proj = EnsureComp<SlingProjectileComponent>(shot);
        proj.BonusDamage = comp.BonusDamage;
        proj.Shooter = shooter;

        // a grenade launched from a sling goes off on its own fuse
        if (TryComp<OnUseTimerTriggerComponent>(shot, out var timer) && !HasComp<ActiveTimerTriggerComponent>(shot))
            _trigger.HandleTimerTrigger(shot, shooter, timer.Delay, timer.BeepInterval, timer.InitialBeepDelay, timer.BeepSound);
    }

    private void OnHit(EntityUid uid, SlingProjectileComponent comp, ThrowDoHitEvent args)
    {
        if (HasComp<DamageableComponent>(args.Target))
            _damageable.TryChangeDamage(args.Target, comp.BonusDamage, origin: comp.Shooter);
        RemCompDeferred<SlingProjectileComponent>(uid);
    }

    private void OnStop(EntityUid uid, SlingProjectileComponent comp, StopThrowEvent args)
    {
        RemCompDeferred<SlingProjectileComponent>(uid);
    }
}
