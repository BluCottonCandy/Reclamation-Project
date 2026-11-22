using System.Numerics;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared._Misfits.Weapons.Ranged.Prediction;
using Robust.Shared.Spawners;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - the psy katana: sheathe and draw it (use in hand), and its sword guard (a hotbar button):
/// drawn and held in both hands, bullets and lasers from the front fly straight back at whoever fired them.
/// 1 stamina a second while the guard is up, 5 more per block; too tired and the guard drops.
/// </summary>
public sealed class MagicPsyBladeSystem : EntitySystem
{
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedItemSystem _items = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly Robust.Shared.Containers.SharedContainerSystem _containers = default!;
    [Dependency] private readonly Robust.Shared.Timing.IGameTiming _timing = default!;
    [Dependency] private readonly Content.Shared.Weapons.Ranged.Systems.SharedGunSystem _gun = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    // The same sounds as the game's sword sheaths (the sabre sheath belt).
    private static readonly SoundSpecifier DrawSound = new SoundPathSpecifier("/Audio/SimpleStation14/Items/Handling/sword_unsheath.ogg");
    private static readonly SoundSpecifier SheatheSound = new SoundPathSpecifier("/Audio/SimpleStation14/Items/Handling/sword_sheath.ogg");
    private static readonly SoundSpecifier BlockSound = new SoundPathSpecifier("/Audio/Weapons/block_metal1.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicPsyBladeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MagicPsyBladeComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<MagicPsyBladeComponent, MagicPsyForgedChangedEvent>(OnForged);
        SubscribeLocalEvent<MagicSwordGuardComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<MagicSwordGuardComponent, MagicSwordGuardEvent>(OnGuardToggle);
        SubscribeLocalEvent<MagicSwordGuardComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<MagicSwordGuardComponent, GotUnequippedHandEvent>(OnLeftHand);
        SubscribeLocalEvent<MagicSwordGuardComponent, ComponentShutdown>(OnGuardShutdown);
        SubscribeLocalEvent<MagicGuardingComponent, ProjectileDeflectAttemptEvent>(OnBullet);
        SubscribeLocalEvent<MagicGuardingComponent, HitScanReflectAttemptEvent>(OnLaser);
    }

    #region Sheathe and draw

    private void OnMapInit(Entity<MagicPsyBladeComponent> ent, ref MapInitEvent args)
    {
        Apply(ent);
    }

    private void OnUseInHand(Entity<MagicPsyBladeComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        // Can't sheathe while both hands are on the hilt: let go with one hand first.
        if (ent.Comp.Drawn && TryComp<WieldableComponent>(ent, out var wield) && wield.Wielded)
            return;
        args.Handled = true;
        ent.Comp.Drawn = !ent.Comp.Drawn;
        Dirty(ent);
        Apply(ent);
        _audio.PlayPvs(ent.Comp.Drawn ? DrawSound : SheatheSound, ent);
        _popup.PopupEntity(Loc.GetString(ent.Comp.Drawn ? "magic-psyblade-draw" : "magic-psyblade-sheathe", ("user", args.User)), args.User);
        if (!ent.Comp.Drawn)
            DropGuard(ent);
    }

    private void OnForged(Entity<MagicPsyBladeComponent> ent, ref MagicPsyForgedChangedEvent args)
    {
        Apply(ent);
    }

    /// <summary>Damage (with any Psy-Forge bonus), in-hand look and icon follow whether the blade is out.</summary>
    private void Apply(Entity<MagicPsyBladeComponent> ent)
    {
        if (TryComp<MeleeWeaponComponent>(ent, out var melee))
        {
            var factor = TryComp<MagicPsyForgedComponent>(ent, out var forged) ? 1f + forged.Bonus : 1f;
            melee.Damage = (ent.Comp.Drawn ? ent.Comp.DrawnDamage : ent.Comp.SheathedDamage) * factor;
            Dirty(ent, melee);
        }
        _items.SetHeldPrefix(ent, ent.Comp.Drawn ? null : ent.Comp.SheathedPrefix);
        _appearance.SetData(ent, MagicPsyBladeVisuals.Drawn, ent.Comp.Drawn);
    }

    #endregion

    #region The guard

    private void OnGetActions(Entity<MagicSwordGuardComponent> ent, ref GetItemActionsEvent args)
    {
        if (!args.InHands)
            return;
        _actionContainer.EnsureAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnGuardToggle(Entity<MagicSwordGuardComponent> ent, ref MagicSwordGuardEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        var user = args.Performer;

        if (ent.Comp.Active)
        {
            DropGuard(ent, "magic-guard-lowered");
            return;
        }
        if (!TryComp<MagicPsyBladeComponent>(ent, out var blade) || !blade.Drawn)
        {
            _magic.Notify(user, Loc.GetString("magic-guard-need-drawn"));
            return;
        }
        if (!TryComp<WieldableComponent>(ent, out var wield) || !wield.Wielded)
        {
            _magic.Notify(user, Loc.GetString("magic-guard-need-wield"));
            return;
        }
        if (TryComp<StaminaComponent>(user, out var stamina) && stamina.Critical)
        {
            _magic.Notify(user, Loc.GetString("magic-guard-too-tired"));
            return;
        }

        ent.Comp.Active = true;
        ent.Comp.Accumulator = 0f;
        Dirty(ent);
        if (ent.Comp.ActionEntity is { } action)
            _actions.SetToggled(action, true);
        var guarding = EnsureComp<MagicGuardingComponent>(user);
        guarding.Sword = ent;
        guarding.SpeedModifier = ent.Comp.SpeedModifier;
        Dirty(user, guarding);
        _speed.RefreshMovementSpeedModifiers(user);
        _magic.Notify(user, Loc.GetString("magic-guard-raised"));
    }

    private void OnUnwielded(Entity<MagicSwordGuardComponent> ent, ref ItemUnwieldedEvent args)
    {
        DropGuard(ent, "magic-guard-lowered");
    }

    private void OnLeftHand(Entity<MagicSwordGuardComponent> ent, ref GotUnequippedHandEvent args)
    {
        DropGuard(ent);
    }

    private void OnGuardShutdown(Entity<MagicSwordGuardComponent> ent, ref ComponentShutdown args)
    {
        DropGuard(ent);
    }

    private void DropGuard(EntityUid sword, string? message = null)
    {
        if (!TryComp<MagicSwordGuardComponent>(sword, out var guard) || !guard.Active)
            return;
        guard.Active = false;
        Dirty(sword, guard);
        if (guard.ActionEntity is { } action)
            _actions.SetToggled(action, false);

        // Whoever was guarding with this sword stops.
        var query = EntityQueryEnumerator<MagicGuardingComponent>();
        while (query.MoveNext(out var user, out var guarding))
        {
            if (guarding.Sword != sword)
                continue;
            RemComp<MagicGuardingComponent>(user);
            _speed.RefreshMovementSpeedModifiers(user);
            if (message != null)
                _magic.Notify(user, Loc.GetString(message));
        }
    }

    /// <summary>Is this coming at the front of the guard (within the arc around where they face)?</summary>
    private bool FromTheFront(EntityUid user, Vector2 travel, float arc)
    {
        if (travel.LengthSquared() < 0.0001f)
            return false;
        var facing = _transform.GetWorldRotation(user).ToWorldVec();
        // the bullet travels toward us, so it comes from the opposite of its travel direction
        var from = -Vector2.Normalize(travel);
        var angle = MathF.Acos(Math.Clamp(Vector2.Dot(from, facing), -1f, 1f)) * 180f / MathF.PI;
        return angle <= arc;
    }

    private bool TryPayBlock(EntityUid user, MagicSwordGuardComponent guard)
    {
        if (TryComp<StaminaComponent>(user, out var stamina) && stamina.Critical)
            return false;
        _stamina.TakeStaminaDamage(user, guard.StaminaPerBlock, visual: false);
        return true;
    }

    private void OnBullet(Entity<MagicGuardingComponent> ent, ref ProjectileDeflectAttemptEvent args)
    {
        if (args.Deflected || !TryComp<MagicSwordGuardComponent>(ent.Comp.Sword, out var guard) || !guard.Active
            || !TryComp<PhysicsComponent>(args.ProjUid, out var body))
            return;
        var velocity = _physics.GetMapLinearVelocity(args.ProjUid, component: body);
        if (!FromTheFront(ent, velocity, guard.FrontArc) || !TryPayBlock(ent, guard))
            return;

        // Straight back at whoever fired it (or back the way it came if they are gone). The bullet is fired again
        // from the guard next tick, like a fresh shot: flipping it in mid-air left it stuck on the shooter's own
        // (predicted) copy, so it never hit anyone.
        var speed = velocity.Length();
        var back = -velocity;
        if (args.CompProj.Shooter is { } shooter && !TerminatingOrDeleted(shooter))
        {
            var toShooter = _transform.GetWorldPosition(shooter) - _transform.GetWorldPosition(ent);
            if (toShooter.LengthSquared() > 0.01f)
                back = Vector2.Normalize(toShooter) * speed;
        }
        _physics.SetLinearVelocity(args.ProjUid, Vector2.Zero, body: body);
        _physics.SetCanCollide(args.ProjUid, false, body: body);
        args.Deflected = true;   // the projectile system makes the guard its new shooter
        _returning.Add((args.ProjUid, ent.Owner, ent.Comp.Sword, back));

        _audio.PlayPvs(BlockSound, ent);
        _popup.PopupEntity(Loc.GetString("magic-guard-blocked", ("user", ent.Owner)), ent);
        _adminLog.Add(LogType.BulletHit, LogImpact.Medium,
            $"{ToPrettyString(ent):player} sent {ToPrettyString(args.ProjUid)} back with a sword guard (shot by {args.CompProj.Shooter})");
    }

    private void OnLaser(Entity<MagicGuardingComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected || !TryComp<MagicSwordGuardComponent>(ent.Comp.Sword, out var guard) || !guard.Active)
            return;
        if (!FromTheFront(ent, args.Direction, guard.FrontArc) || !TryPayBlock(ent, guard))
            return;
        args.Direction = -args.Direction;
        args.Reflected = true;
        _audio.PlayPvs(BlockSound, ent);
        _popup.PopupEntity(Loc.GetString("magic-guard-blocked", ("user", ent.Owner)), ent);
    }

    #endregion

    /// <summary>Bullets a guard just stopped, waiting to be fired back this tick: bullet, guard, sword, velocity.</summary>
    private readonly List<(EntityUid Bullet, EntityUid Guard, EntityUid Sword, Vector2 Velocity)> _returning = new();

    /// <summary>Fires each stopped bullet back from the guard, as their shot (it can't hit them).</summary>
    private void FireBack()
    {
        foreach (var (bullet, guard, sword, velocity) in _returning)
        {
            if (TerminatingOrDeleted(bullet) || TerminatingOrDeleted(guard) || velocity.LengthSquared() < 0.01f)
                continue;
            RemComp<PredictedProjectileServerComponent>(bullet);   // everyone sees it now, the first shooter too
            if (TryComp<ProjectileComponent>(bullet, out var projectile))
            {
                projectile.Shooter = guard;
                projectile.Weapon = sword;
                projectile.IgnoreShooter = true;
                projectile.DamagedEntity = false;
                Dirty(bullet, projectile);
            }
            _transform.SetCoordinates(bullet, Transform(guard).Coordinates);
            _transform.AttachToGridOrMap(bullet);
            _physics.SetCanCollide(bullet, true);
            EnsureComp<TimedDespawnComponent>(bullet).Lifetime = 3f;
            _gun.ShootProjectile(bullet, Vector2.Normalize(velocity), Vector2.Zero, sword, guard, velocity.Length());
        }
        _returning.Clear();
    }

    /// <summary>
    /// Magic items left loose crumble to ash after a few seconds. Loose = not inside anything (a hand, a slot or a
    /// bag); a blade stuck in someone counts as loose. Only the item is deleted; the ash is spawned on the floor at
    /// its spot (never inside or on the person it was stuck in).
    /// </summary>
    private void AshLooseItems()
    {
        var now = _timing.CurTime;
        var crumble = new List<(EntityUid Item, MagicAshWhenLooseComponent Ash, EntityUid? Holder)>();
        var query = EntityQueryEnumerator<MagicAshWhenLooseComponent>();
        while (query.MoveNext(out var uid, out var ash))
        {
            if (_containers.IsEntityInContainer(uid))
            {
                // Held, worn or in a bag: fine for the summoner. Anyone else: it crumbles in their hands at once.
                var holder = Holder(uid);
                if (holder != null && ash.Owner is { } owner && holder != owner)
                {
                    crumble.Add((uid, ash, holder));
                    continue;
                }
                if (holder != null)
                {
                    ash.LooseSince = null;
                    continue;
                }
                // In a crate or a bag nobody carries: counts as left behind.
            }
            ash.LooseSince ??= now;
            if (now - ash.LooseSince.Value >= TimeSpan.FromSeconds(ash.Delay))
                crumble.Add((uid, ash, null));
        }
        foreach (var (uid, ash, holder) in crumble)
        {
            // The ash goes on the floor: at the holder's feet, or where the blade lay (never inside anyone).
            var spot = _transform.GetMapCoordinates(holder ?? uid);
            if (holder is { } who)
                _popup.PopupEntity(Loc.GetString("magic-psyblade-ash-hands"), who, who, PopupType.MediumCaution);
            else
                _popup.PopupCoordinates(Loc.GetString("magic-psyblade-ash"), _transform.ToCoordinates(spot));
            QueueDel(uid);
            Spawn(ash.Ash, spot);
        }
    }

    /// <summary>The living person holding, wearing or carrying (in a bag) this item, if any.</summary>
    private EntityUid? Holder(EntityUid item)
    {
        var current = item;
        for (var i = 0; i < 8 && _containers.TryGetContainingContainer((current, null, null), out var container); i++)
        {
            current = container.Owner;
            if (HasComp<Content.Shared.Mobs.Components.MobStateComponent>(current))
                return current;
        }
        return null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        FireBack();
        AshLooseItems();
        // 1 stamina a second while a guard is up; worn out = the guard drops.
        var query = EntityQueryEnumerator<MagicGuardingComponent>();
        var drop = new List<(EntityUid, EntityUid)>();
        while (query.MoveNext(out var user, out var guarding))
        {
            if (!TryComp<MagicSwordGuardComponent>(guarding.Sword, out var guard) || !guard.Active)
            {
                drop.Add((user, guarding.Sword));
                continue;
            }
            guard.Accumulator += frameTime;
            if (guard.Accumulator < 1f)
                continue;
            guard.Accumulator -= 1f;
            if (TryComp<StaminaComponent>(user, out var stamina) && stamina.Critical)
            {
                drop.Add((user, guarding.Sword));
                continue;
            }
            _stamina.TakeStaminaDamage(user, guard.StaminaPerSecond, visual: false);
        }
        foreach (var (user, sword) in drop)
        {
            if (Exists(sword))
                DropGuard(sword, "magic-guard-too-tired");
            else
            {
                RemComp<MagicGuardingComponent>(user);
                _speed.RefreshMovementSpeedModifiers(user);
            }
        }
    }
}
