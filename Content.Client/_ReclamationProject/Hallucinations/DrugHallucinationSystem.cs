using System.Numerics;
using Content.Client.Weather;
using Content.Shared.Weather;
using Content.Client.Examine;
using Content.Client.Viewport;
using Content.Shared.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Content.Shared.Nutrition.Components;
using Content.Shared.Interaction;
using Content.Client.Interaction;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Content.Shared._ReclamationProject.Hallucinations;
using Content.Shared.Drugs;
using Content.Shared.CombatMode;
using Content.Shared.Movement.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Client.Weapons.Melee;
using Robust.Client.GameObjects;
using Robust.Client.Audio;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Hallucinations;

/// <summary>
/// Private visual encounters. Like client-side casing effects, these entities never exist on the server.
/// Enemy definitions supply attack visuals and cadence; real combat and damage are never invoked.
/// </summary>
public sealed class DrugHallucinationSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transforms = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly MeleeWeaponSystem _melee = default!;

    // Choose a family before its variant so color variants do not overwhelm the encounter pool.
    private static readonly string[][] Types =
    {
        new[] { "ReclamationPhantomGhoul", "ReclamationPhantomGhoulReaver", "ReclamationPhantomGhoulRotter" },
        new[] { "ReclamationPhantomRaider", "ReclamationPhantomRaiderRanged" },
        new[] { "ReclamationPhantomMutant" },
        new[] { "ReclamationPhantomDeathclaw" },
        new[] { "ReclamationPhantomMolerat" },
        new[] { "ReclamationPhantomAssaultronHostile" },
        new[] { "ReclamationPhantomProtectronHostile", "ReclamationPhantomProtectronBuilderHostile", "ReclamationPhantomProtectronFireHostile", "ReclamationPhantomProtectronPoliceHostile" },
        new[] { "ReclamationPhantomSecuritronHostile", "ReclamationPhantomSecuritronBlueHostile", "ReclamationPhantomSecuritronBrownHostile", "ReclamationPhantomSecuritronRedHostile", "ReclamationPhantomSecuritronYellowHostile" },
        new[] { "ReclamationPhantomMrHandyClaw", "ReclamationPhantomMrHandySaw", "ReclamationPhantomMrHandyGun" },
        new[] { "ReclamationPhantomMrHandyGutsy" },
        new[] { "ReclamationPhantomRobobrain" },
        new[] { "ReclamationPhantomEyebot" },
        new[] { "ReclamationPhantomSentryBot", "ReclamationPhantomSentryBotBallistic" },
    };
    [Dependency] private readonly WeatherSystem _weather = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly ExamineSystem _examine = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    private EntityUid? _hovered;
    private ShaderInstance? _hoverInRange;
    private ShaderInstance? _hoverOutOfRange;
    private readonly List<Phantom> _phantoms = new();
    private EntityUid? _owner;
    private MapId _map;
    private bool _damagingEncounter;
    private Guid _encounter;
    private TimeSpan _nextPainHitAt;
    private bool _wasHigh;
    private bool _highEncounter;
    private TimeSpan _highAt;
    private TimeSpan? _unstableAt;
    private static readonly string[] Foods =
    {
        "ReclamationPhantomFood0",
        "ReclamationPhantomFood1",
        "ReclamationPhantomFood2",
        "ReclamationPhantomFood3",
        "ReclamationPhantomFood4",
        "ReclamationPhantomFood5",
        "ReclamationPhantomFood6",
        "ReclamationPhantomFood7",
        "ReclamationPhantomFood8",
        "ReclamationPhantomFood9",
        "ReclamationPhantomFood10",
        "ReclamationPhantomFood11",
        "ReclamationPhantomFood12",
        "ReclamationPhantomFood13",
        "ReclamationPhantomFood14",
        "ReclamationPhantomFood15",
        "ReclamationPhantomFood16",
        "ReclamationPhantomFood17",
    };
    private static readonly string[] Drinks =
    {
        "ReclamationPhantomDrink0",
        "ReclamationPhantomDrink1",
    };
    private readonly List<VisualTemptation> _temptations = new();
    private bool _consumeTemptationRelease;
    private TimeSpan _foodAt;
    private TimeSpan _drinkAt;
    private sealed record VisualTemptation(EntityUid Uid, TimeSpan Expires, HallucinationKind Kind);
    private const float Speed = 3.5f;
    private const float Reach = 0.85f;

    private sealed record Phantom(EntityUid Uid, TimeSpan Expires, bool FromHigh,
        MeleeWeaponComponent? Melee, GunComponent? Gun, SpriteMovementComponent? Movement)
    {
        public TimeSpan NextAttack;
        public TimeSpan? VanishAt;
        public TimeSpan NextVoice;
        public bool? Moving;
    }

    private sealed record VisualShot(EntityUid Uid, Vector2 Velocity, TimeSpan Expires, bool CanHit = false, EntityUid? Shooter = null);
    private readonly List<VisualShot> _shots = new();

    public override void Initialize()
    {
        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnTemptationUse, false, true),
                new[] { typeof(DragDropSystem), typeof(SharedInteractionSystem) })
            .BindBefore(ContentKeyFunctions.ExamineEntity,
                new PointerInputCmdHandler(OnIllusionExamine, outsidePrediction: true), new[] { typeof(ExamineSystem) })
            .Register<DrugHallucinationSystem>();
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnShot);
        SubscribeLocalEvent<CombatModeComponent, MeleeAttackEvent>(OnMelee);
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<DrugHallucinationSystem>();
        Clear();
        _hoverInRange?.Dispose();
        _hoverOutOfRange?.Dispose();
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        var user = _player.LocalEntity;
        if (user == null || !TryComp<TransformComponent>(user, out var transform) ||
            transform.MapID == MapId.Nullspace)
        {
            Reset();
            return;
        }
        if (_owner != user)
        {
            Reset();
            _owner = user;
        }
        if (_map != transform.MapID)
        {
            Clear();
            _map = transform.MapID;
        }
        var now = _timing.CurTime;
        var high = !HasComp<DrugHallucinationResistanceComponent>(user) &&
            (HasComp<SeeingRainbowsComponent>(user) || HasComp<DrugHighComponent>(user));
        var unstable = HasComp<UnstableComponent>(user);
        if (high && !_wasHigh)
        {
            _highEncounter = false;
            // Guarantee the encounter within the high marker's five-second minimum lifetime.
            _highAt = now + TimeSpan.FromSeconds(_random.NextFloat() * 2);
        }
        _wasHigh = high;
        var awake = TryComp<MobStateComponent>(user, out var mob) && mob.CurrentState == MobState.Alive;
        if (!awake)
            Clear();
        if (awake && high && !_highEncounter && now >= _highAt)
        {
            _highEncounter = SpawnEncounter(user.Value, true);
            // Retry obstructed screens gently, but never create a second encounter during this high.
            _highAt = now + TimeSpan.FromSeconds(3);
        }
        if (unstable)
        {
            _unstableAt ??= now + NextPerkDelay();
            if (awake && !high && now >= _unstableAt)
            {
                if (HallucinationRules.Occurs(false, _random.NextFloat()))
                    SpawnEncounter(user.Value, false);
                _unstableAt = now + NextPerkDelay();
            }
        }
        else
            _unstableAt = null;

        // Appetite illusions are independent: their own timers, no probability roll, no enemy quota.
        if (awake && (high || unstable))
        {
            if (TryComp<HungerComponent>(user, out var hunger) && hunger.CurrentThreshold <= HungerThreshold.Peckish && now >= _foodAt)
            {
                SpawnTemptations(user.Value, HallucinationKind.Food);
                _foodAt = now + HallucinationRules.AppetiteCooldown;
            }
            if (TryComp<ThirstComponent>(user, out var thirst) && thirst.CurrentThirstThreshold <= ThirstThreshold.Thirsty && now >= _drinkAt)
            {
                SpawnTemptations(user.Value, HallucinationKind.Drink);
                _drinkAt = now + HallucinationRules.AppetiteCooldown;
            }
        }
        for (var i = _temptations.Count - 1; i >= 0; i--)
        {
            var temptation = _temptations[i];
            if (!Deleted(temptation.Uid) && now < temptation.Expires) continue;
            if (!Deleted(temptation.Uid)) EntityManager.DeleteEntity(temptation.Uid);
            _temptations.RemoveAt(i);
        }
        var target = _transforms.GetWorldPosition(transform);
        for (var i = _phantoms.Count - 1; i >= 0; i--)
        {
            var phantom = _phantoms[i];
            if (Deleted(phantom.Uid) || now >= phantom.Expires ||
                phantom.VanishAt is { } vanish && now >= vanish)
            {
                Remove(i);
                continue;
            }
            var voice = Comp<HallucinationEnemyComponent>(phantom.Uid);
            if (now >= phantom.NextVoice)
            {
                _audio.PlayLocal(voice.IdleVoice, phantom.Uid, user.Value);
                phantom.NextVoice = now + VoiceDelay(voice);
            }
            var xform = Transform(phantom.Uid);
            var position = _transforms.GetWorldPosition(xform);
            var offset = target - position;
            if (offset.LengthSquared() > 0.001f)
                _transforms.SetWorldRotation(phantom.Uid, offset.ToAngle());
            if (phantom.VanishAt != null)
                continue;
            var distance = offset.Length();
            SetMoving(phantom, phantom.VanishAt == null && distance > (phantom.Gun != null ? 5.5f : phantom.Melee?.Range ?? Reach));
            if (phantom.Gun is { } gun)
            {
                // Ranged enemies stalk their target and fire using their existing weapon cadence and sounds.
                if (distance > 5.5f)
                    MoveThroughWalls(phantom.Uid, xform, position, offset, frameTime);
                if (distance <= 9f && now >= phantom.NextAttack)
                {
                    FireVisualShot(phantom, position, target, user.Value);
                    phantom.NextAttack = now + TimeSpan.FromSeconds(1f / Math.Max(0.1f, gun.FireRate));
                }
            }
            else if (distance <= (phantom.Melee?.Range ?? Reach))
            {
                if (now < phantom.NextAttack) continue;
                // Play only the real enemy's lunge/arc and sound, never its damage-producing attack method.
                var melee = phantom.Melee;
                _melee.DoLunge(phantom.Uid, phantom.Uid, melee?.Angle ?? Angle.Zero,
                    offset, melee?.Animation.Id ?? "WeaponArcPunch");
                if (melee != null)
                    _audio.PlayLocal(melee.SoundHit ?? melee.SoundSwing, phantom.Uid, user.Value);
                if (_damagingEncounter) ReportHit(false);
                else phantom.VanishAt = now + TimeSpan.FromSeconds(0.18);
                phantom.NextAttack = now + TimeSpan.FromSeconds(1f / Math.Max(0.1f, melee?.AttackRate ?? 1f));
            }
            else
                MoveThroughWalls(phantom.Uid, xform, position, offset, frameTime);
        }
        UpdateHover(user.Value);
        CheckOwnedProjectiles(user.Value, transform.MapID, frameTime);
        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            if (Deleted(shot.Uid) || now >= shot.Expires)
            {
                if (!Deleted(shot.Uid)) EntityManager.DeleteEntity(shot.Uid);
                _shots.RemoveAt(i);
                continue;
            }
            var xform = Transform(shot.Uid);
            var start = _transforms.GetWorldPosition(xform);
            var step = shot.Velocity * frameTime;
            var fraction = step.LengthSquared() < 0.0001f ? 0 : Math.Clamp(Vector2.Dot(target - start, step) / step.LengthSquared(), 0f, 1f);
            if (shot.CanHit && Vector2.DistanceSquared(target, start + step * fraction) <= 0.45f * 0.45f)
            {
                if (_damagingEncounter) ReportHit(true);
                else if (shot.Shooter is { } shooter)
                {
                    var index = _phantoms.FindIndex(p => p.Uid == shooter);
                    if (index >= 0) Remove(index);
                }
                EntityManager.DeleteEntity(shot.Uid);
                _shots.RemoveAt(i);
                continue;
            }
            _transforms.SetWorldPosition((shot.Uid, xform), start + step);
        }
    }

    private TimeSpan NextPerkDelay() => TimeSpan.FromSeconds(_random.Next(HallucinationRules.MinimumMinutes * 60, HallucinationRules.MaximumMinutes * 60 + 1));

    public bool SpawnEncounter(EntityUid user, bool fromHigh, string? forcedPrototype = null, bool? damaging = null)
    {
        if (forcedPrototype == null && damaging == null && _random.Prob(0.25f))
            return SpawnWeather(user);
        _weather.StopHallucination();
        ClearEnemies(); // Enemy encounters never clear independent food or drink illusions.
        _damagingEncounter = damaging ?? _random.Prob(0.5f);
        var xform = Transform(user);
        var origin = _transforms.GetWorldPosition(xform);
        var bounds = _eye.GetWorldViewbounds().CalcBoundingBox();
        var prototype = forcedPrototype != null && Types.Any(family => family.Contains(forcedPrototype))
            ? forcedPrototype : _random.Pick(_random.Pick(Types));
        var count = _random.Next(1, 4);
        for (var i = 0; i < count; i++)
        {
            Vector2? position = null;
            // Hallucinations can begin behind obstructions and pass through them; always use the screen perimeter.
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var t = _random.NextFloat();
                const float inset = 0.5f;
                var candidate = _random.Next(4) switch
                {
                    0 => new Vector2(bounds.Left + inset, bounds.Bottom + t * bounds.Height),
                    1 => new Vector2(bounds.Right - inset, bounds.Bottom + t * bounds.Height),
                    2 => new Vector2(bounds.Left + t * bounds.Width, bounds.Top - inset),
                    _ => new Vector2(bounds.Left + t * bounds.Width, bounds.Bottom + inset),
                };
                if (Vector2.DistanceSquared(origin, candidate) < 9)
                    continue;
                position = candidate;
                break;
            }
            if (position == null)
                continue;
            var coords = _transforms.ToCoordinates(new MapCoordinates(position.Value, xform.MapID));
            var uid = Spawn(prototype, coords);
            _transforms.SetWorldRotation(uid, (origin - position.Value).ToAngle());
            var source = _prototypes.Index(Comp<HallucinationEnemyComponent>(uid).Source);
            _metadata.SetEntityDescription(uid, source.Description);
            var melee = source.Components.TryGetValue("MeleeWeapon", out var meleeEntry)
                ? meleeEntry.Component as MeleeWeaponComponent : null;
            var gun = source.Components.TryGetValue("Gun", out var gunEntry)
                ? gunEntry.Component as GunComponent : null;
            var movement = source.Components.TryGetValue("SpriteMovement", out var movementEntry)
                ? movementEntry.Component as SpriteMovementComponent : null;
            var enemy = Comp<HallucinationEnemyComponent>(uid);
            _audio.PlayLocal(enemy.AggroVoice ?? enemy.IdleVoice, uid, user);
            _phantoms.Add(new Phantom(uid, _timing.CurTime + TimeSpan.FromSeconds(20), fromHigh, melee, gun, movement)
            {
                NextVoice = _timing.CurTime + VoiceDelay(enemy),
            });
        }
        if (_phantoms.Count > 0 && _damagingEncounter)
        {
            _encounter = Guid.NewGuid();
            _nextPainHitAt = TimeSpan.Zero;
            RaiseNetworkEvent(new HallucinationEncounterStartedEvent(_encounter));
        }
        return _phantoms.Count > 0;
    }

    public bool SpawnWeather(EntityUid user)
    {
        var variants = _prototypes.EnumeratePrototypes<WeatherPrototype>().Where(p => p.Sprite != null).ToArray();
        if (variants.Length == 0) return false;
        ClearEnemies();
        return _weather.StartHallucination(user, _random.Pick(variants), HallucinationRules.WeatherDuration);
    }

    /// <summary>Only visual shells are spawned: no item, solution, nutrition or pickup components.</summary>
    public bool SpawnTemptations(EntityUid user, HallucinationKind kind)
    {
        for (var i = _temptations.Count - 1; i >= 0; i--)
        {
            if (_temptations[i].Kind != kind) continue;
            if (!Deleted(_temptations[i].Uid)) EntityManager.DeleteEntity(_temptations[i].Uid);
            _temptations.RemoveAt(i);
        }
        var spawned = false;
        var xform = Transform(user);
        var origin = _transforms.GetWorldPosition(xform);
        var pool = kind == HallucinationKind.Drink ? Drinks : Foods;
        var count = _random.Next(1, 4);
        for (var i = 0; i < count; i++)
        {
            for (var attempt = 0; attempt < 24; attempt++)
            {
                var angle = _random.NextFloat() * MathF.Tau;
                var point = origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (2f + _random.NextFloat() * 2f);
                if (!ClearPath(origin, point, user, xform.MapID)) continue;
                var uid = Spawn(_random.Pick(pool), _transforms.ToCoordinates(new MapCoordinates(point, xform.MapID)));
                _temptations.Add(new VisualTemptation(uid, _timing.CurTime + TimeSpan.FromSeconds(20), kind));
                spawned = true;
                break;
            }
        }
        if (!spawned)
        {
            // A confined room must not suppress the guaranteed encounter during a high.
            var uid = Spawn(_random.Pick(pool), xform.Coordinates);
            _temptations.Add(new VisualTemptation(uid, _timing.CurTime + TimeSpan.FromSeconds(20), kind));
        }
        return true;
    }

    // Pick only our own visual shells; never make them network interaction targets.
    private EntityUid? FindHoveredIllusion()
    {
        if (_ui.CurrentlyHovered is not IViewportControl viewport || !_input.MouseScreenPosition.IsValid)
            return null;
        var aim = viewport.PixelToMap(_input.MouseScreenPosition.Position);
        EntityUid? best = null;
        var distance = float.MaxValue;
        foreach (var uid in _phantoms.Select(p => p.Uid).Concat(_temptations.Select(p => p.Uid)))
        {
            if (Deleted(uid) || !TryComp<SpriteComponent>(uid, out var sprite) || !sprite.Visible) continue;
            var xform = Transform(uid);
            if (xform.MapID != aim.MapId) continue;
            var position = _transforms.GetWorldPosition(xform);
            var bounds = _sprites.CalculateBounds((uid, sprite), position,
                _transforms.GetWorldRotation(xform), _eye.CurrentEye.Rotation);
            var delta = Vector2.DistanceSquared(aim.Position, position);
            if (!bounds.Contains(aim.Position) || delta >= distance) continue;
            best = uid;
            distance = delta;
        }
        return best;
    }

    private void ClearHover()
    {
        if (_hovered is { } old && !Deleted(old) && TryComp<SpriteComponent>(old, out var sprite) &&
            (sprite.PostShader == _hoverInRange || sprite.PostShader == _hoverOutOfRange))
            sprite.PostShader = null;
        _hovered = null;
    }

    private void UpdateHover(EntityUid user)
    {
        var hovered = FindHoveredIllusion();
        if (hovered != _hovered) ClearHover();
        _hovered = hovered;
        if (hovered is not { } uid || !TryComp<SpriteComponent>(uid, out var sprite)) return;
        var origin = _transforms.GetWorldPosition(Transform(user));
        var position = _transforms.GetWorldPosition(Transform(uid));
        var inRange = Vector2.DistanceSquared(origin, position) <= SharedInteractionSystem.InteractionRangeSquared &&
            ClearPath(origin, position, user, Transform(user).MapID);
        _hoverInRange ??= _prototypes.Index<ShaderPrototype>("SelectionOutlineInrange").InstanceUnique();
        _hoverOutOfRange ??= _prototypes.Index<ShaderPrototype>("SelectionOutline").InstanceUnique();
        var shader = inRange ? _hoverInRange : _hoverOutOfRange;
        shader.SetParameter("outline_width", (float)_eye.MainViewport.GetRenderScale());
        sprite.PostShader = shader;
    }

    private bool OnIllusionExamine(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (_player.LocalEntity == null || FindHoveredIllusion() is not { } uid) return false;
        if (args.State == BoundKeyState.Down)
            _examine.DoExamine(uid); // The existing examine system handles client-only entities locally.
        return true;
    }

    private bool OnTemptationUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State == BoundKeyState.Up && _consumeTemptationRelease)
        {
            _consumeTemptationRelease = false;
            return true;
        }
        if (args.State != BoundKeyState.Down || _player.LocalEntity is not { } user || (_temptations.Count == 0 && _phantoms.Count == 0))
            return false;
        var selected = FindHoveredIllusion();
        var xform = Transform(user);
        var origin = _transforms.GetWorldPosition(xform);
        if (selected == null) return false;
        var enemyIndex = _phantoms.FindIndex(p => p.Uid == selected);
        if (enemyIndex >= 0)
        {
            var enemyPosition = _transforms.GetWorldPosition(Transform(selected.Value));
            if (Vector2.DistanceSquared(origin, enemyPosition) > SharedInteractionSystem.InteractionRangeSquared ||
                !ClearPath(origin, enemyPosition, user, xform.MapID)) return false;
            Remove(enemyIndex);
            _consumeTemptationRelease = true;
            return true;
        }
        for (var i = _temptations.Count - 1; i >= 0; i--)
        {
            var uid = _temptations[i].Uid;
            if (Deleted(uid)) continue;
            var position = _transforms.GetWorldPosition(Transform(uid));
            if (uid != selected ||
                Vector2.DistanceSquared(position, origin) > SharedInteractionSystem.InteractionRange * SharedInteractionSystem.InteractionRange ||
                !ClearPath(origin, position, user, xform.MapID)) continue;
            _temptations.RemoveAt(i);
            EntityManager.DeleteEntity(uid);
            _consumeTemptationRelease = true;
            return true; // Never send a pickup/interact request for a client-only entity to the server.
        }
        return false;
    }

    private TimeSpan VoiceDelay(HallucinationEnemyComponent enemy)
    {
        var min = Math.Max(1f, enemy.MinVoiceDelay);
        var max = Math.Max(min, enemy.MaxVoiceDelay);
        return TimeSpan.FromSeconds(min + _random.NextFloat() * (max - min));
    }

    private void SetMoving(Phantom phantom, bool moving)
    {
        if (phantom.Moving == moving || phantom.Movement == null ||
            !TryComp<SpriteComponent>(phantom.Uid, out var sprite)) return;
        phantom.Moving = moving;
        var layers = moving ? phantom.Movement.MovementLayers : phantom.Movement.NoMovementLayers;
        foreach (var (key, state) in layers)
            sprite.LayerSetData(key, state);
    }

    private void CheckOwnedProjectiles(EntityUid user, MapId map, float frameTime)
    {
        if (_phantoms.Count == 0) return;
        var projectiles = EntityQueryEnumerator<ProjectileComponent, TransformComponent, PhysicsComponent>();
        while (projectiles.MoveNext(out _, out var projectile, out var xform, out var physics))
            if (projectile.Shooter == user && xform.MapID == map)
                HitByOwnedObject(_transforms.GetWorldPosition(xform), physics.LinearVelocity, frameTime);
        var thrown = EntityQueryEnumerator<ThrownItemComponent, TransformComponent, PhysicsComponent>();
        while (thrown.MoveNext(out _, out var item, out var xform, out var physics))
            if (item.Thrower == user && xform.MapID == map)
                HitByOwnedObject(_transforms.GetWorldPosition(xform), physics.LinearVelocity, frameTime);
    }

    private void HitByOwnedObject(Vector2 position, Vector2 velocity, float frameTime)
    {
        var start = position - velocity * frameTime;
        var segment = position - start;
        for (var i = _phantoms.Count - 1; i >= 0; i--)
        {
            if (Deleted(_phantoms[i].Uid)) continue;
            var target = _transforms.GetWorldPosition(Transform(_phantoms[i].Uid));
            var fraction = segment.LengthSquared() < 0.0001f ? 0f : Math.Clamp(Vector2.Dot(target - start, segment) / segment.LengthSquared(), 0f, 1f);
            if (Vector2.DistanceSquared(target, start + segment * fraction) <= 0.55f * 0.55f)
                Remove(i);
        }
    }

    private void MoveThroughWalls(EntityUid uid, TransformComponent xform, Vector2 position, Vector2 offset, float frameTime)
    {
        if (offset.LengthSquared() < 0.001f) return;
        var step = Vector2.Normalize(offset) * Math.Min(Speed * frameTime, offset.Length());
        _transforms.SetWorldPosition((uid, xform), position + step);
    }

    private void FireVisualShot(Phantom phantom, Vector2 origin, Vector2 target, EntityUid user)
    {
        var delta = target - origin;
        if (delta.LengthSquared() < 0.001f) return;
        var direction = Vector2.Normalize(delta);
        var coordinates = _transforms.ToCoordinates(new MapCoordinates(origin, Transform(user).MapID));
        var bullet = Spawn(Comp<HallucinationEnemyComponent>(phantom.Uid).Projectile, coordinates);
        _transforms.SetWorldRotation(bullet, direction.ToAngle());
        _shots.Add(new VisualShot(bullet, direction * 20f, _timing.CurTime + TimeSpan.FromSeconds(1.5), true, phantom.Uid));
        var flash = Spawn("MuzzleFlashEffect", coordinates);
        _transforms.SetWorldRotation(flash, direction.ToAngle());
        _shots.Add(new VisualShot(flash, Vector2.Zero, _timing.CurTime + TimeSpan.FromSeconds(0.12)));
        _audio.PlayLocal(phantom.Gun?.SoundGunshot, phantom.Uid, user);
    }

    private void OnMelee(EntityUid user, CombatModeComponent combat, ref MeleeAttackEvent args)
    {
        if (_phantoms.Count == 0 || user != _player.LocalEntity || !_timing.IsFirstTimePredicted ||
            !TryComp<MeleeWeaponComponent>(args.Weapon, out var weapon)) return;
        var origin = _transforms.GetWorldPosition(Transform(user));
        var aim = _eye.PixelToMap(_input.MouseScreenPosition);
        if (aim.MapId != Transform(user).MapID) return;
        var direction = aim.Position - origin;
        if (direction.LengthSquared() < 0.001f) return;
        direction = Vector2.Normalize(direction);
        var reach = _melee.GetMeleeReach(user, weapon);
        // Normal successful attack events include empty-space punches and weapon swings.
        // The phantom never becomes a network attack target or enters the real damage pipeline.
        for (var i = _phantoms.Count - 1; i >= 0; i--)
        {
            if (Deleted(_phantoms[i].Uid)) continue;
            var point = _transforms.GetWorldPosition(Transform(_phantoms[i].Uid));
            var delta = point - origin;
            var distance = delta.Length();
            if (distance > reach + 0.45f || distance < 0.001f ||
                Vector2.Dot(delta / distance, direction) < 0.65f ||
                !ClearPath(origin, point, user, Transform(user).MapID)) continue;
            Remove(i);
        }
    }

    private bool ClearPath(Vector2 from, Vector2 to, EntityUid user, MapId map)
    {
        var delta = to - from;
        if (delta.LengthSquared() < 0.0001f)
            return true;
        var ray = new CollisionRay(from, Vector2.Normalize(delta),
            (int)(CollisionGroup.Impassable | CollisionGroup.HighImpassable));
        return !_physics.IntersectRayWithPredicate(map, ray, user, static (hit, ignored) => hit == ignored,
            delta.Length(), true).Any();
    }

    private void OnShot(EntityUid uid, GunComponent component, ref GunShotEvent args)
    {
        if (_phantoms.Count == 0 || args.User != _player.LocalEntity || !_timing.IsFirstTimePredicted || args.Ammo.Count == 0)
            return;
        var xform = Transform(args.User);
        var origin = _transforms.GetWorldPosition(xform);
        var aim = _eye.PixelToMap(_input.MouseScreenPosition);
        if (aim.MapId != xform.MapID || Vector2.DistanceSquared(origin, aim.Position) < 0.001f)
            return;
        var direction = Vector2.Normalize(aim.Position - origin);
        var nearest = -1;
        var distance = float.MaxValue;
        for (var i = 0; i < _phantoms.Count; i++)
        {
            if (Deleted(_phantoms[i].Uid))
                continue;
            var point = _transforms.GetWorldPosition(Transform(_phantoms[i].Uid));
            var along = Vector2.Dot(point - origin, direction);
            if (along <= 0 || along > 30 || along >= distance ||
                Vector2.DistanceSquared(point, origin + direction * along) > 0.55f * 0.55f ||
                !ClearPath(origin, point, args.User, xform.MapID))
                continue;
            nearest = i;
            distance = along;
        }
        if (nearest >= 0)
            Remove(nearest);
    }

    private void ReportHit(bool ranged)
    {
        if (_encounter == Guid.Empty || _timing.CurTime < _nextPainHitAt) return;
        _nextPainHitAt = _timing.CurTime + TimeSpan.FromSeconds(0.15);
        RaiseNetworkEvent(new HallucinationHitEvent(_encounter, ranged));
    }

    private void EndEncounter()
    {
        if (_encounter == Guid.Empty) return;
        RaiseNetworkEvent(new HallucinationEncounterEndedEvent(_encounter));
        _encounter = Guid.Empty;
    }

    private void Remove(int index)
    {
        var phantom = _phantoms[index];
        if (_hovered == phantom.Uid) ClearHover();
        _phantoms.RemoveAt(index);
        if (_phantoms.Count == 0) EndEncounter();
        if (!Deleted(phantom.Uid))
            EntityManager.DeleteEntity(phantom.Uid);
    }

    private void Clear()
    {
        _weather.StopHallucination();
        ClearHover();
        foreach (var temptation in _temptations)
            if (!Deleted(temptation.Uid)) EntityManager.DeleteEntity(temptation.Uid);
        _temptations.Clear();
        ClearEnemies();
    }

    private void ClearEnemies()
    {
        EndEncounter();
        _damagingEncounter = false;
        for (var i = _phantoms.Count - 1; i >= 0; i--)
            Remove(i);
        foreach (var shot in _shots)
            if (!Deleted(shot.Uid)) EntityManager.DeleteEntity(shot.Uid);
        _shots.Clear();
    }

    private void Reset()
    {
        Clear();
        _consumeTemptationRelease = false;
        _foodAt = TimeSpan.Zero;
        _drinkAt = TimeSpan.Zero;
        _owner = null;
        _wasHigh = false;
        _highEncounter = false;
        _unstableAt = null;
    }
}
