using System.Numerics;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - Rock Bullet. Each cast summons a heated rock that circles you (up to 4, 60 s each).
/// A left click in combat mode fires every stored rock at the mouse. It listens to the click itself (the light
/// attack request), so it works with empty hands, with a melee weapon, and even while the swing is on cooldown.
/// Rock damage is fixed (40 each): Empowered casts never add to it.
/// </summary>
public sealed class MagicRockBulletSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly Content.Shared.CombatMode.SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;

    private const string RockVisual = "MagicOrbitingRock";
    private const float OrbitRadius = 0.7f;
    private const float OrbitSpeed = 2.5f; // radians per second
    private const float Spread = 0.12f;    // radians between rocks in a volley

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicSummonRockEvent>(OnSummon);
        // The client sends this on a left click in combat mode (melee only reaches ~1 tile, so it can't be used).
        SubscribeNetworkEvent<MagicFireRocksEvent>(OnFireRequest);
        SubscribeLocalEvent<MagicStoredRocksComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnSummon(MagicSummonRockEvent args)
    {
        if (args.Handled)
            return;

        var caster = args.Performer;
        var stored = EnsureComp<MagicStoredRocksComponent>(caster);
        if (stored.Rocks.Count >= args.Max)
        {
            _magic.Notify(caster, Loc.GetString("magic-rocks-full", ("max", args.Max)));
            return;
        }

        if (!_magic.TryPayForSpell(args.Action, caster))
            return;
        args.Handled = true;

        var rock = Spawn(RockVisual, Transform(caster).Coordinates);
        _transform.SetParent(rock, caster);
        stored.Rocks.Add(rock);
        stored.Expires.Add(_timing.CurTime + TimeSpan.FromSeconds(args.Lifetime));
        stored.Count = stored.Rocks.Count;
        Dirty(caster, stored);
        stored.Projectile = args.Projectile;
        _magic.Notify(caster, Loc.GetString("magic-rocks-summoned", ("count", stored.Rocks.Count), ("max", args.Max)));
    }

    /// <summary>Left click in combat mode: every stored rock flies at the clicked spot.</summary>
    private void OnFireRequest(MagicFireRocksEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } caster
            || !TryComp<MagicStoredRocksComponent>(caster, out var stored)
            || stored.Rocks.Count == 0
            || !_combat.IsInCombatMode(caster)
            || _mobState.IsIncapacitated(caster))
            return;

        var origin = _transform.GetMapCoordinates(caster);
        var aim = _transform.ToMapCoordinates(GetCoordinates(msg.Target));
        var facing = aim.MapId == origin.MapId && (aim.Position - origin.Position).LengthSquared() > 0.01f
            ? Vector2.Normalize(aim.Position - origin.Position)
            : _transform.GetWorldRotation(caster).ToWorldVec();
        Fire((caster, stored), facing);
    }

    /// <summary>Every stored rock flies in <paramref name="facing"/> (a small fan when there are several).</summary>
    public void Fire(Entity<MagicStoredRocksComponent> ent, Vector2 facing)
    {
        if (ent.Comp.Rocks.Count == 0 || ent.Comp.Projectile is not { } proto)
            return;

        var caster = ent.Owner;
        var count = ent.Comp.Rocks.Count;
        for (var i = 0; i < count; i++)
        {
            QueueDel(ent.Comp.Rocks[i]);
            var angle = (i - (count - 1) / 2f) * Spread;
            var dir = new Angle(angle).RotateVec(facing);
            var shot = Spawn(proto, Transform(caster).Coordinates);
            if (TryComp<MagicOnHitComponent>(shot, out var onHit))
            {
                // Fixed strength: an Empowered cast never makes the rocks hit harder.
                onHit.Caster = caster;
                onHit.Power = 1f;
            }
            _gun.ShootProjectile(shot, dir, Vector2.Zero, caster, caster, 25f);
        }

        ent.Comp.Rocks.Clear();
        ent.Comp.Expires.Clear();
        ent.Comp.Count = 0;
        Dirty(ent);
    }

    private void OnShutdown(Entity<MagicStoredRocksComponent> ent, ref ComponentShutdown args)
    {
        foreach (var rock in ent.Comp.Rocks)
            QueueDel(rock);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MagicStoredRocksComponent>();
        while (query.MoveNext(out var uid, out var stored))
        {
            if (stored.Rocks.Count == 0)
                continue;

            // Rocks that ran out of time crumble.
            for (var i = stored.Rocks.Count - 1; i >= 0; i--)
            {
                if (stored.Expires[i] > now && !Deleted(stored.Rocks[i]))
                    continue;
                QueueDel(stored.Rocks[i]);
                stored.Rocks.RemoveAt(i);
                stored.Expires.RemoveAt(i);
                _magic.Notify(uid, Loc.GetString("magic-rocks-crumble"));
                stored.Count = stored.Rocks.Count;
                Dirty(uid, stored);
            }

            // The rest circle the caster.
            stored.Angle += OrbitSpeed * frameTime;
            for (var i = 0; i < stored.Rocks.Count; i++)
            {
                var a = stored.Angle + MathF.Tau * i / stored.Rocks.Count;
                _transform.SetLocalPosition(stored.Rocks[i], new Vector2(MathF.Cos(a), MathF.Sin(a)) * OrbitRadius);
            }
        }
    }
}
