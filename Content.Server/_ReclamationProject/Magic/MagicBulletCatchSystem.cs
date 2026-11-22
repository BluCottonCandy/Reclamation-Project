using System.Numerics;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Administration.Logs;
using Content.Shared.CombatMode;
using Content.Shared.Database;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - Bullet Catch (Psychic, King). Cast to start: every bullet that would hit you stops and
/// hovers in a ring around you, as many as come. 1 mana a second while it is up (overcast damage at 0 mana).
/// A left click in combat mode throws every caught bullet at the mouse. Cast again (or get knocked down) to stop:
/// bullets still hovering then drop harmlessly.
/// </summary>
public sealed class MagicBulletCatchSystem : EntitySystem
{
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly Robust.Shared.Containers.SharedContainerSystem _containers = default!;

    private const float ThrowSpeed = 25f;
    private const float Cone = 0.5f;              // the whole thrown volley fits in this cone (radians, about 30 degrees)
    private const int Visible = 24;               // bullets shown hovering; any more are held out of sight
    private const string HiddenContainer = "magic_caught_bullets";
    private const float ThrownLifetime = 6f;

    /// <summary>Bullets caught this tick: parked next frame (changing collisions inside a collision is unsafe).</summary>
    private readonly List<(EntityUid Bullet, EntityUid Catcher)> _toPark = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicBulletCatchEvent>(OnCast);
        SubscribeLocalEvent<MagicBulletCatcherComponent, ProjectileDeflectAttemptEvent>(OnBullet);
        SubscribeNetworkEvent<MagicFireRocksEvent>(OnThrowRequest);
        SubscribeLocalEvent<MagicBulletCatcherComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnCast(MagicBulletCatchEvent args)
    {
        if (args.Handled)
            return;
        var user = args.Performer;

        if (TryComp<MagicBulletCatcherComponent>(user, out var existing) && existing.Catching)
        {
            args.Handled = true;
            Stop((user, existing), "magic-catch-end");
            return;
        }
        if (!_magic.TryPayForSpell(args.Action, user))
            return;
        args.Handled = true;

        var catcher = EnsureComp<MagicBulletCatcherComponent>(user);
        catcher.Catching = true;
        catcher.DrainPerSecond = args.DrainPerSecond;
        catcher.Accumulator = 0f;
        Dirty(user, catcher);
        _magic.Notify(user, Loc.GetString("magic-catch-start"), PopupType.Medium);
    }

    private void OnBullet(Entity<MagicBulletCatcherComponent> ent, ref ProjectileDeflectAttemptEvent args)
    {
        if (args.Deflected || !ent.Comp.Catching || !TryComp<PhysicsComponent>(args.ProjUid, out var body))
            return;
        _physics.SetLinearVelocity(args.ProjUid, Vector2.Zero, body: body);
        args.Deflected = true;   // no hit; the projectile system makes the catcher its shooter
        _toPark.Add((args.ProjUid, ent.Owner));
    }

    /// <summary>Parks a caught bullet: no collisions, never despawns, circling its catcher.</summary>
    private void Park(EntityUid bullet, EntityUid catcherUid)
    {
        if (TerminatingOrDeleted(bullet) || !TryComp<MagicBulletCatcherComponent>(catcherUid, out var catcher))
            return;
        _physics.SetLinearVelocity(bullet, Vector2.Zero);
        _physics.SetCanCollide(bullet, false);
        RemComp<TimedDespawnComponent>(bullet);
        // The first ones hover in a small circle; past that they're held out of sight (still counted, still thrown).
        if (catcher.Caught.Count < Visible)
            _transform.SetParent(bullet, catcherUid);
        else
            _containers.Insert(bullet, _containers.EnsureContainer<Robust.Shared.Containers.Container>(catcherUid, HiddenContainer));
        catcher.Caught.Add(bullet);
        catcher.Count = catcher.Caught.Count;
        Dirty(catcherUid, catcher);
        // One message for the first catch, then every 10th (not one per bullet).
        if (catcher.Count == 1 || catcher.Count % 10 == 0)
            _popup.PopupEntity(Loc.GetString("magic-catch-caught", ("count", catcher.Count)), catcherUid, catcherUid);
    }

    /// <summary>Left click in combat mode (the same signal Rock Bullet uses): every caught bullet flies at the mouse.</summary>
    private void OnThrowRequest(MagicFireRocksEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user
            || !TryComp<MagicBulletCatcherComponent>(user, out var catcher)
            || catcher.Caught.Count == 0
            || !_combat.IsInCombatMode(user)
            || _mobState.IsIncapacitated(user))
            return;

        var origin = _transform.GetMapCoordinates(user);
        var aim = _transform.ToMapCoordinates(GetCoordinates(msg.Target));
        var facing = aim.MapId == origin.MapId && (aim.Position - origin.Position).LengthSquared() > 0.01f
            ? Vector2.Normalize(aim.Position - origin.Position)
            : _transform.GetWorldRotation(user).ToWorldVec();

        var count = catcher.Caught.Count;
        for (var i = 0; i < count; i++)
        {
            var bullet = catcher.Caught[i];
            if (TerminatingOrDeleted(bullet))
                continue;
            if (_containers.TryGetContainingContainer((bullet, null, null), out var held))
                _containers.Remove(bullet, held);
            _transform.SetCoordinates(bullet, Transform(user).Coordinates);
            _transform.AttachToGridOrMap(bullet);
            _physics.SetCanCollide(bullet, true);
            EnsureComp<TimedDespawnComponent>(bullet).Lifetime = ThrownLifetime;
            if (TryComp<ProjectileComponent>(bullet, out var projectile))
            {
                projectile.Shooter = user;
                projectile.Weapon = user;
                projectile.IgnoreShooter = true;
                Dirty(bullet, projectile);
            }
            var angle = count > 1 ? (i / (count - 1f) - 0.5f) * Cone : 0f;
            _gun.ShootProjectile(bullet, new Angle(angle).RotateVec(facing), Vector2.Zero, user, user, ThrowSpeed);
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(user):player} threw back {count} caught bullets");
        catcher.Caught.Clear();
        catcher.Count = 0;
        Dirty(user, catcher);
        if (!catcher.Catching)
            RemComp<MagicBulletCatcherComponent>(user);
    }

    private void Stop(Entity<MagicBulletCatcherComponent> ent, string message)
    {
        // Bullets still hovering drop harmlessly.
        foreach (var bullet in ent.Comp.Caught)
        {
            if (!TerminatingOrDeleted(bullet))
                QueueDel(bullet);
        }
        ent.Comp.Caught.Clear();
        _magic.Notify(ent, Loc.GetString(message));
        RemComp<MagicBulletCatcherComponent>(ent);
    }

    private void OnShutdown(Entity<MagicBulletCatcherComponent> ent, ref ComponentShutdown args)
    {
        foreach (var bullet in ent.Comp.Caught)
        {
            if (!TerminatingOrDeleted(bullet))
                QueueDel(bullet);
        }
        ent.Comp.Caught.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (bullet, catcher) in _toPark)
            Park(bullet, catcher);
        _toPark.Clear();

        var stop = new List<EntityUid>();
        var query = EntityQueryEnumerator<MagicBulletCatcherComponent, MagicCasterComponent>();
        while (query.MoveNext(out var uid, out var catcher, out var caster))
        {
            if (catcher.Catching)
            {
                if (_mobState.IsIncapacitated(uid))
                {
                    stop.Add(uid);
                    continue;
                }
                catcher.Accumulator += frameTime;
                if (catcher.Accumulator >= 1f)
                {
                    catcher.Accumulator -= 1f;
                    // From mana; at 0 mana it is overcasting, which hurts (the usual mana debuff).
                    _magic.PayMana((uid, caster), catcher.DrainPerSecond);
                }
            }

            // The visible caught bullets circle you close in, two small rings of 12.
            catcher.Caught.RemoveAll(b => TerminatingOrDeleted(b));
            catcher.Angle += 1.5f * frameTime;
            var shown = Math.Min(Visible, catcher.Caught.Count);
            for (var i = 0; i < shown; i++)
            {
                var ring = i / 12;
                var inRing = Math.Min(12, shown - ring * 12);
                var a = catcher.Angle * (ring % 2 == 0 ? 1f : -1f) + MathF.Tau * (i % 12) / inRing;
                var radius = 0.45f + 0.2f * ring;
                _transform.SetLocalPosition(catcher.Caught[i], new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
            }
        }
        foreach (var uid in stop)
        {
            if (TryComp<MagicBulletCatcherComponent>(uid, out var catcher))
                Stop((uid, catcher), "magic-catch-broken");
        }
    }
}
