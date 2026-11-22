using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Electrocution;
using Content.Server.Emp;
using Content.Shared._Misfits.Special;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Administration.Logs;
using Content.Shared.Cuffs;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Drunk;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Forensics;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Jittering;
using Content.Shared.Language.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Chat;
using Robust.Server.GameObjects;
using Content.Shared.Roles.Jobs;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.StatusEffect;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Projectiles;
using Content.Shared.Bed.Sleep;
using Robust.Shared.Spawners;
using Content.Shared.Speech.Muting;
using Content.Server.Spawners.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - runs the spell kit (see MagicSpellKit.cs): touch, area, zone, conjure and
/// projectile spells, lingering zones, timed magic conditions and the special spells.
/// </summary>
public sealed class MagicSpellKitSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly MagicGrimoireSystem _grimoire = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly Content.Server._ReclamationProject.PowerArmor.FusionCorePoweredSystem _fusionCore = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly ElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly SharedCuffableSystem _cuffs = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly SharedStutteringSystem _stutter = default!;
    [Dependency] private readonly SharedDrunkSystem _drunk = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly Content.Server.EUI.EuiManager _eui = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly MagicMirrorImageSystem _mirrorImages = default!;
    [Dependency] private readonly MagicFloorFireSystem _floorFire = default!;

    private readonly List<(TimeSpan When, Action Run)> _delayed = new();
    /// <summary>When each person can next be stunned by a magic shock.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _shockStunnedUntil = new();
    private readonly List<(TimeSpan When, EntityUid Light)> _lightsOff = new();
    private readonly List<(TimeSpan When, EntityUid Who, string Name)> _falseFaces = new();
    private bool _bondTransfer;

    private static readonly string[] ImplantLines =
    {
        "magic-implant-1", "magic-implant-2", "magic-implant-3", "magic-implant-4", "magic-implant-5", "magic-implant-6",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicTouchSpellEvent>(OnTouch);
        SubscribeLocalEvent<MagicAreaSpellEvent>(OnArea);
        SubscribeLocalEvent<MagicZoneSpellEvent>(OnZone);
        SubscribeLocalEvent<MagicConjureSpellEvent>(OnConjure);
        SubscribeLocalEvent<MagicOnHitComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<MagicOnHitComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<MagicOnHitComponent, ThrowDoHitEvent>(OnThrowHit);
        SubscribeLocalEvent<MagicConjuredComponent, DroppedEvent>(OnConjuredDropped);
        SubscribeLocalEvent<MagicConditionsComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<MagicConditionsComponent, AttackAttemptEvent>(OnAttackAttempt);
    }

    #region Helpers

    /// <summary>1.5 when the page was written in the true word order.</summary>
    public float PowerOf(EntityUid action)
    {
        return TryComp<MagicSpellActionComponent>(action, out var spell) && spell.Empowered ? 1.5f : 1f;
    }

    private bool Pay(EntityUid action, EntityUid performer) => _magic.TryPayForSpell(action, performer);

    private bool IsHostile(MagicHit hit) =>
        hit.Damage != null || hit.Stun > 0 || hit.Knockdown > 0 || hit.Slow > 0 || hit.Sleep > 0 || hit.Blind > 0
        || hit.Mute > 0 || hit.Confuse > 0 || hit.Push > 0 || hit.Pull > 0 || hit.Disarm || hit.Shock > 0
        || hit.FireStacks > 0 || hit.Blood < 0 || hit.Bleed > 0 || hit.Mana < 0;

    /// <summary>People (and anything with a mob state) in range of a point.</summary>
    private List<EntityUid> MobsNear(MapCoordinates center, float radius)
    {
        var list = new List<EntityUid>();
        // Radius 0 = a self-only spell: nobody else. (A zero range asserts and crashes the debug server.)
        if (radius <= 0f)
            return list;
        foreach (var uid in _lookup.GetEntitiesInRange<MobStateComponent>(center, radius))
            list.Add(uid);
        return list;
    }

    private List<EntityUid> ItemsNear(MapCoordinates center, float radius)
    {
        var list = new List<EntityUid>();
        if (radius <= 0f)
            return list;
        foreach (var uid in _lookup.GetEntitiesInRange<ItemComponent>(center, radius))
        {
            if (!Transform(uid).Anchored && !_inventoryOrHeld(uid))
                list.Add(uid);
        }
        return list;
    }

    private bool _inventoryOrHeld(EntityUid uid)
    {
        return Transform(uid).ParentUid is { Valid: true } parent && HasComp<MobStateComponent>(parent);
    }

    /// <summary>Picks targets in a circle, line or cone.</summary>
    private List<EntityUid> InShape(EntityUid caster, MapCoordinates origin, MapCoordinates click, MagicShape shape, float radius, float length, bool items)
    {
        var result = new List<EntityUid>();
        if (shape == MagicShape.Circle)
        {
            result.AddRange(MobsNear(click, radius));
            if (items)
                result.AddRange(ItemsNear(click, radius));
            return result;
        }

        var dir = click.Position - origin.Position;
        if (dir.LengthSquared() < 0.01f)
            dir = new Vector2(0, 1);
        dir = Vector2.Normalize(dir);
        var candidates = MobsNear(origin, length);
        if (items)
            candidates.AddRange(ItemsNear(origin, length));

        foreach (var uid in candidates)
        {
            var pos = _transform.GetMapCoordinates(uid);
            if (pos.MapId != origin.MapId)
                continue;
            var rel = pos.Position - origin.Position;
            var along = Vector2.Dot(rel, dir);
            if (along < 0 || along > length)
                continue;
            if (shape == MagicShape.Line)
            {
                var side = MathF.Abs(rel.X * dir.Y - rel.Y * dir.X);
                if (side <= MathF.Max(radius, 0.8f))
                    result.Add(uid);
            }
            else if (rel.LengthSquared() < 0.01f || Vector2.Dot(Vector2.Normalize(rel), dir) >= 0.70f)
            {
                result.Add(uid);
            }
        }
        return result;
    }

    private void Throw(EntityUid target, Vector2 from, float tiles, bool toward, EntityUid caster)
    {
        if (tiles <= 0)
            return;
        var pos = _transform.GetMapCoordinates(target).Position;
        var dir = toward ? from - pos : pos - from;
        if (dir.LengthSquared() < 0.01f)
            dir = _random.NextAngle().ToVec();
        var distance = toward ? MathF.Min(tiles, dir.Length() - 0.8f) : tiles;
        if (distance <= 0.1f)
            return;
        var coords = Transform(target).Coordinates.Offset(Vector2.Normalize(dir) * distance);
        _throwing.TryThrow(target, coords, 8f, caster, recoil: false, playSound: false);
    }

    private void Spawn(EntProtoId? proto, EntityCoordinates coords, int count, float spread, EntityUid caster, float power)
    {
        if (proto is not { } id)
            return;
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            var offset = spread > 0 ? _random.NextVector2(spread) : Vector2.Zero;
            var ent = Spawn(id, coords.Offset(offset));
            if (TryComp<MagicAuraComponent>(ent, out var aura))
            {
                aura.Caster = caster;
                aura.Power = power;
            }
            if (TryComp<MagicServantComponent>(ent, out var servant))
                MakeServant((ent, servant), caster);
            // Mirror Image / Shell Game: the placeholder becomes a real-looking copy of the caster.
            if (HasComp<MagicMirrorImageComponent>(ent))
                _mirrorImages.MakeLookalike(caster, ent);
        }
    }

    #endregion

    #region Applying a hit

    /// <summary>Applies everything in a MagicHit to one target.</summary>
    public void ApplyHit(EntityUid target, MagicHit hit, float power, EntityUid? caster, Vector2? center = null)
    {
        if (Deleted(target))
            return;

        // Mind Shield / wards stop hostile spells from other people.
        if (caster != target && IsHostile(hit) && TryUseCondition(target, MagicCondition.Ward))
        {
            _popup.PopupEntity(Loc.GetString("magic-ward-blocked"), target, Filter.PvsExcept(target), true, PopupType.Medium);
            _magic.Notify(target, Loc.GetString("magic-ward-blocked"), PopupType.Medium);
            return;
        }

        // Everything this hit does goes into the target's chat as one line, so nothing gets missed.
        if (DescribeHit(hit, power) is { } parts)
            _magic.Notify(target, Loc.GetString(caster == target ? "magic-hit-summary-self" : "magic-hit-summary", ("parts", parts)));

        var time = (float seconds) => TimeSpan.FromSeconds(seconds * power);

        if (hit.Damage != null)
            _damageable.TryChangeDamage(target, hit.Damage * power, ignoreResistances: hit.IgnoreArmor, origin: caster);
        if (hit.Heal != null)
            _damageable.TryChangeDamage(target, hit.Heal * -power, true, false, origin: caster, canSever: false); // same as medkits under shitmed

        if (hit.Stun > 0)
            _stun.TryStun(target, time(hit.Stun), true);
        if (hit.Knockdown > 0 && !HasCondition(target, MagicCondition.Steady))
            _stun.TryKnockdown(target, time(hit.Knockdown), true);
        if (hit.Slow > 0)
            _stun.TrySlowdown(target, time(hit.Slow), true, hit.SlowAmount, hit.SlowAmount);
        if (hit.Sleep > 0)
            _status.TryAddStatusEffect<ForcedSleepingComponent>(target, "ForcedSleep", time(hit.Sleep), true);
        if (hit.Drowsy > 0)
        {
            // A second dose while already drowsy puts them to sleep.
            if (HasComp<Content.Shared.Drowsiness.DrowsinessComponent>(target))
                _status.TryAddStatusEffect<ForcedSleepingComponent>(target, "ForcedSleep", time(5f), true);
            _status.TryAddStatusEffect<Content.Shared.Drowsiness.DrowsinessComponent>(target, "Drowsiness", time(hit.Drowsy), true);
        }
        if (hit.Blind > 0)
            _status.TryAddStatusEffect(target, "TemporaryBlindness", time(hit.Blind), true, "TemporaryBlindness");
        if (hit.Mute > 0)
            _status.TryAddStatusEffect<MutedComponent>(target, "Muted", time(hit.Mute), true);
        if (hit.Pacify > 0)
            _status.TryAddStatusEffect(target, "Pacified", time(hit.Pacify), true, "Pacified");
        if (hit.Confuse > 0)
        {
            _status.TryAddStatusEffect(target, "SeeingRainbows", time(hit.Confuse), true, "SeeingRainbows");
            _drunk.TryApplyDrunkenness(target, hit.Confuse * power, false);
        }
        if (hit.Jitter > 0)
            _jitter.DoJitter(target, time(hit.Jitter), true);
        if (hit.Stutter > 0)
            _stutter.DoStutter(target, time(hit.Stutter), true);

        if (hit.Extinguish)
            _flammable.Extinguish(target);
        if (hit.FireStacks > 0)
        {
            _flammable.AdjustFireStacks(target, hit.FireStacks * power);
            _flammable.Ignite(target, caster ?? target);
        }

        if (hit.Bleed != 0)
            _bloodstream.TryModifyBleedAmount(target, hit.Bleed * (hit.Bleed > 0 ? power : 1f));
        if (hit.Blood != 0)
            _bloodstream.TryModifyBloodLevel(target, FixedPoint2.New(hit.Blood * power));

        if ((hit.Mana != 0 || hit.Strain != 0) && TryComp<MagicCasterComponent>(target, out var targetCaster))
        {
            if (hit.Mana != 0)
                targetCaster.MagicMana = Math.Clamp(targetCaster.MagicMana + hit.Mana * power, 0f, targetCaster.MagicMaxMana);
            if (hit.Strain != 0)
                targetCaster.MagicStrain = Math.Clamp(targetCaster.MagicStrain + hit.Strain, 0f, 100f);
            Dirty(target, targetCaster);
        }

        if (hit.Disarm || hit.DisarmGuns)
            Disarm(target, hit.DisarmGuns, caster);

        if (hit.Shock > 0)
        {
            // A shock stuns for 1.5 s, but only once every 5 s per person, so puddles and storm clouds
            // can't stun-lock anyone. Inside that window the shock still does its damage.
            var now = _timing.CurTime;
            if (!_shockStunnedUntil.TryGetValue(target, out var until) || until <= now)
            {
                _shockStunnedUntil[target] = now + TimeSpan.FromSeconds(5);
                _electrocution.TryDoElectrocution(target, caster, (int) (hit.Shock * power), TimeSpan.FromSeconds(1.5), true, ignoreInsulation: true);
            }
            else
            {
                _damageable.TryChangeDamage(target, new DamageSpecifier(_proto.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Shock"), hit.Shock * power), true, origin: caster);
            }
        }

        if (hit.Wet > 0)
            AddCondition(target, MagicCondition.Wet, hit.Wet * power, 1f, caster);

        if (hit.Message is { } message)
            _magic.Notify(target, Loc.GetString(message), PopupType.MediumCaution);

        if (hit.Effects.Count > 0)
        {
            var args = new EntityEffectReagentArgs(target, EntityManager, null, null, FixedPoint2.New(power), null, null, FixedPoint2.New(power));
            foreach (var effect in hit.Effects)
                effect.Effect(args);
        }

        if (hit.Condition != MagicCondition.None)
            AddCondition(target, hit.Condition, hit.ConditionTime * power, hit.ConditionPower * (hit.Condition is MagicCondition.Absorb ? power : 1f), caster);

        if (center is { } c)
        {
            if (hit.Push > 0)
                Throw(target, c, hit.Push * power, false, caster ?? target);
            if (hit.Pull > 0)
                Throw(target, c, hit.Pull * power, true, caster ?? target);
        }
    }

    /// <summary>"18 cold, stunned 2 s, slowed 6 s" - or null when the hit does nothing you'd notice.</summary>
    private string? DescribeHit(MagicHit hit, float power)
    {
        var parts = new List<string>();
        string S(float seconds) => MathF.Round(seconds * power, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (hit.Damage != null)
        {
            foreach (var (type, amount) in hit.Damage.DamageDict)
            {
                if (amount > 0)
                    parts.Add(Loc.GetString("magic-part-damage", ("amount", (int) MathF.Round(amount.Float() * power)), ("type", type.ToLowerInvariant())));
            }
        }
        if (hit.Heal != null && hit.Heal.GetTotal() > 0)
            parts.Add(Loc.GetString("magic-part-heal", ("amount", (int) MathF.Round(hit.Heal.GetTotal().Float() * power))));
        if (hit.Stun > 0) parts.Add(Loc.GetString("magic-part-stun", ("s", S(hit.Stun))));
        if (hit.Knockdown > 0) parts.Add(Loc.GetString("magic-part-knockdown", ("s", S(hit.Knockdown))));
        if (hit.Slow > 0) parts.Add(Loc.GetString("magic-part-slow", ("s", S(hit.Slow))));
        if (hit.Sleep > 0) parts.Add(Loc.GetString("magic-part-sleep", ("s", S(hit.Sleep))));
        if (hit.Drowsy > 0) parts.Add(Loc.GetString("magic-part-drowsy", ("s", S(hit.Drowsy))));
        if (hit.Blind > 0) parts.Add(Loc.GetString("magic-part-blind", ("s", S(hit.Blind))));
        if (hit.Mute > 0) parts.Add(Loc.GetString("magic-part-mute", ("s", S(hit.Mute))));
        if (hit.Pacify > 0) parts.Add(Loc.GetString("magic-part-pacify", ("s", S(hit.Pacify))));
        if (hit.Confuse > 0) parts.Add(Loc.GetString("magic-part-confuse", ("s", S(hit.Confuse))));
        if (hit.Jitter > 0) parts.Add(Loc.GetString("magic-part-jitter", ("s", S(hit.Jitter))));
        if (hit.Stutter > 0) parts.Add(Loc.GetString("magic-part-stutter", ("s", S(hit.Stutter))));
        if (hit.FireStacks > 0) parts.Add(Loc.GetString("magic-part-fire"));
        if (hit.Extinguish) parts.Add(Loc.GetString("magic-part-extinguish"));
        if (hit.Bleed > 0) parts.Add(Loc.GetString("magic-part-bleed"));
        if (hit.Bleed < 0) parts.Add(Loc.GetString("magic-part-bleed-stop"));
        if (hit.Blood < 0) parts.Add(Loc.GetString("magic-part-blood-lost", ("amount", (int) MathF.Round(-hit.Blood * power))));
        if (hit.Blood > 0) parts.Add(Loc.GetString("magic-part-blood-gain", ("amount", (int) MathF.Round(hit.Blood * power))));
        if (hit.Mana < 0) parts.Add(Loc.GetString("magic-part-mana-lost", ("amount", (int) MathF.Round(-hit.Mana * power))));
        if (hit.Mana > 0) parts.Add(Loc.GetString("magic-part-mana-gain", ("amount", (int) MathF.Round(hit.Mana * power))));
        if (hit.Disarm) parts.Add(Loc.GetString("magic-part-disarm"));
        if (hit.DisarmGuns) parts.Add(Loc.GetString("magic-part-disarm-guns"));
        if (hit.Shock > 0) parts.Add(Loc.GetString("magic-part-shock", ("amount", (int) MathF.Round(hit.Shock * power))));
        if (hit.Wet > 0) parts.Add(Loc.GetString("magic-part-wet", ("s", S(hit.Wet))));
        if (hit.Push > 0) parts.Add(Loc.GetString("magic-part-push"));
        if (hit.Pull > 0) parts.Add(Loc.GetString("magic-part-pull"));
        if (hit.Condition != MagicCondition.None)
            parts.Add(Loc.GetString("magic-part-condition", ("name", Loc.GetString($"magic-condition-{hit.Condition.ToString().ToLowerInvariant()}")),
                ("s", S(hit.ConditionTime))));

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private void Disarm(EntityUid target, bool gunsOnly, EntityUid? caster)
    {
        if (!TryComp<HandsComponent>(target, out var hands))
            return;
        foreach (var held in _hands.EnumerateHeld(target, hands).ToList())
        {
            if (gunsOnly && !HasComp<GunComponent>(held))
                continue;
            if (!_hands.TryDrop(target, held, checkActionBlocker: false))
                continue;
            var coords = Transform(held).Coordinates.Offset(_random.NextAngle().ToVec() * 3f);
            _throwing.TryThrow(held, coords, 6f, caster, recoil: false, playSound: false);
        }
    }

    #endregion

    #region Spell events

    private void OnTouch(MagicTouchSpellEvent args)
    {
        if (args.Handled)
            return;
        if (args.DeadOnly && !_mobState.IsDead(args.Target))
        {
            _magic.Notify(args.Performer, Loc.GetString("magic-needs-dead"));
            return;
        }
        if (!Pay(args.Action, args.Performer))
            return;
        args.Handled = true;

        var power = PowerOf(args.Action);
        var caster = args.Performer;
        var target = args.Target;
        var casterPos = _transform.GetMapCoordinates(caster).Position;

        if (args.Self != null)
            ApplyHit(caster, args.Self, power, caster);

        void Land()
        {
            // A delayed spell lands seconds later: by then the caster or target may be gone (gibbed, deleted).
            if (TerminatingOrDeleted(target) || TerminatingOrDeleted(caster))
                return;
            var targetPos = _transform.GetMapCoordinates(target);
            // Pull goes toward the caster; push goes away from them.
            _magic.Flash(target, _magic.ClassOf(args.Action), "Hit");
            ApplyHit(target, args.Hit, power, caster, args.Hit.Pull > 0 ? _transform.GetMapCoordinates(caster).Position : casterPos);
            if (args.BurstRadius > 0 && args.Burst != null)
            {
                // If the main hit already hurt the target (Arc Lightning), the splash only jumps to the people
                // around them; if the splash IS the damage (Sky Strike, Fission Mind) the target takes it too.
                var mainHitHurts = args.Hit.Damage != null || args.Hit.Shock > 0;
                foreach (var other in MobsNear(targetPos, args.BurstRadius))
                {
                    if (other != caster && !(mainHitHurts && other == target))
                        ApplyHit(other, args.Burst, power, caster, targetPos.Position);
                }
            }
            Spawn(args.Spawn, Transform(target).Coordinates, args.SpawnCount, 0.5f, caster, power);
            DoSpecial(args.Special, caster, target, Transform(target).Coordinates, power, null);
        }

        if (args.Delay > 0)
            _delayed.Add((_timing.CurTime + TimeSpan.FromSeconds(args.Delay), Land));
        else
            Land();

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(caster):player} cast a touch spell on {ToPrettyString(target):target}");
    }

    private void OnArea(MagicAreaSpellEvent args)
    {
        if (args.Handled || !Pay(args.Action, args.Performer))
            return;
        args.Handled = true;

        var power = PowerOf(args.Action);
        var caster = args.Performer;
        var center = _transform.GetMapCoordinates(caster);
        var radius = args.Radius * (power > 1f ? 1.2f : 1f);

        if (args.Self != null)
            ApplyHit(caster, args.Self, power, caster);

        var cls = _magic.ClassOf(args.Action);
        if (cls is { } ringClass)
            _magic.FlashRing(Transform(caster).Coordinates, ringClass, radius);

        // Fire bursts set the floor around you alight (never your own tile).
        if (radius > 0 && _floorFire.IsFireSpell(args.Action))
            _floorFire.FireDisc(center, radius, center);

        var targets = MobsNear(center, radius);
        if (args.Items)
            targets.AddRange(ItemsNear(center, radius));
        foreach (var target in targets)
        {
            if (target == caster && !args.IncludeSelf)
                continue;
            if (HasComp<MobStateComponent>(target))
                _magic.Flash(target, cls, "Hit");
            ApplyHit(target, args.Hit, power, caster, center.Position);
        }

        Spawn(args.Spawn, Transform(caster).Coordinates, args.SpawnCount, args.SpawnSpread, caster, power);
        DoSpecial(args.Special, caster, null, Transform(caster).Coordinates, power, targets, radius);
    }

    private void OnZone(MagicZoneSpellEvent args)
    {
        if (args.Handled)
            return;
        if (args.Special == MagicSpecial.TeleportCasterDark && !IsDark(args.Target))
        {
            _magic.Notify(args.Performer, Loc.GetString("magic-shadow-step-light"));
            return;
        }
        if (!Pay(args.Action, args.Performer))
            return;
        args.Handled = true;

        var power = PowerOf(args.Action);
        var caster = args.Performer;
        var origin = _transform.GetMapCoordinates(caster);
        var click = _transform.ToMapCoordinates(args.Target);
        var coords = args.Target;

        if (args.Self != null)
            ApplyHit(caster, args.Self, power, caster);

        // Teleports happen first so "teleport, then blast where you land" works.
        if (args.Special is MagicSpecial.TeleportCaster or MagicSpecial.TeleportCasterDark)
        {
            var leftFrom = Transform(caster).Coordinates;
            _transform.SetCoordinates(caster, coords);
            _transform.AttachToGridOrMap(caster);
            _magic.TeleportMishap(caster, leftFrom);
            origin = _transform.GetMapCoordinates(caster);
            click = origin;
        }

        void Land()
        {
            if (TerminatingOrDeleted(caster))
                return;
            var targets = InShape(caster, origin, click, args.Shape, args.Radius * (power > 1f ? 1.2f : 1f), args.Length, args.Items);
            // Fire zones, cones and lines (Flame Wave...) leave the floor burning where they reach.
            if (_floorFire.IsFireSpell(args.Action))
                _floorFire.FireShape(origin, click, args.Shape, args.Radius, args.Length);
            var center = args.Shape == MagicShape.Circle ? click.Position : origin.Position;
            foreach (var target in targets)
            {
                if (target == caster && !args.IncludeSelf)
                    continue;
                ApplyHit(target, args.Hit, power, caster, center);
            }

            if (args.Shape == MagicShape.Circle)
            {
                Spawn(args.Spawn, coords, args.SpawnCount, args.SpawnSpread, caster, power);
            }
            else if (args.Spawn != null)
            {
                // Lines and cones leave their spawn along the way.
                var dir = click.Position - origin.Position;
                if (dir.LengthSquared() < 0.01f)
                    dir = new Vector2(0, 1);
                dir = Vector2.Normalize(dir);
                var steps = Math.Max(1, args.SpawnCount);
                for (var i = 1; i <= steps; i++)
                    Spawn(args.Spawn, Transform(caster).Coordinates.Offset(dir * (args.Length * i / steps)), 1, args.SpawnSpread, caster, power);
            }

            DoSpecial(args.Special, caster, null, coords, power, targets, args.Radius);
        }

        if (args.Delay > 0)
        {
            Spawn("MagicZoneWarning", coords);
            _delayed.Add((_timing.CurTime + TimeSpan.FromSeconds(args.Delay), Land));
        }
        else
        {
            Land();
        }
    }

    private void OnConjure(MagicConjureSpellEvent args)
    {
        if (args.Handled || !Pay(args.Action, args.Performer))
            return;
        args.Handled = true;

        var power = PowerOf(args.Action);
        var caster = args.Performer;
        if (args.Self != null)
            ApplyHit(caster, args.Self, power, caster);

        for (var i = 0; i < args.Count; i++)
        {
            var item = Spawn(args.Item, Transform(caster).Coordinates);
            var conjured = EnsureComp<MagicConjuredComponent>(item);
            if (TryComp<MagicAshWhenLooseComponent>(item, out var ashOwner))
                ashOwner.Owner = caster; // only the summoner may keep it
            conjured.VanishOnDrop = args.VanishOnDrop && args.EquipSlot == null;
            if (args.Lifetime > 0f) // 0 = lasts until something else ends it (the psy katana turns to ash when left loose)
                EnsureComp<TimedDespawnComponent>(item).Lifetime = args.Lifetime * power;

            if (args.EquipSlot is { } slot && _inventory.TryEquip(caster, item, slot, silent: true, force: true))
                continue;
            if (!_hands.TryPickupAnyHand(caster, item))
                conjured.VanishOnDrop = false; // hands full: it just lies there until it fades
        }
        _magic.Notify(caster, Loc.GetString("magic-conjured"));
    }

    private void OnProjectileHit(Entity<MagicOnHitComponent> ent, ref ProjectileHitEvent args)
    {
        var comp = ent.Comp;
        var caster = comp.Caster ?? args.Shooter;
        if (comp.Caster == null && caster is { } shooter && TryComp<MagicCasterComponent>(shooter, out var shooterCaster))
            comp.Power = shooterCaster.LastPower;
        var from = _transform.GetMapCoordinates(ent);
        ApplyHit(args.Target, comp.Hit, comp.Power, caster, from.Position);
        if (comp.BurstRadius > 0 && comp.Burst != null)
        {
            var at = _transform.GetMapCoordinates(args.Target);
            foreach (var other in MobsNear(at, comp.BurstRadius))
            {
                if (other != args.Target && other != caster)
                    ApplyHit(other, comp.Burst, comp.Power, caster, at.Position);
            }
        }
        Spawn(comp.Spawn, Transform(args.Target).Coordinates, 1, 0f, caster ?? ent.Owner, comp.Power);
        if (caster is { } c)
            DoSpecial(comp.Special, c, args.Target, Transform(args.Target).Coordinates, comp.Power, null);
    }

    /// <summary>Conjured melee weapons: every person hit gets the weapon's magic hit.</summary>
    private void OnMeleeHit(Entity<MagicOnHitComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;
        var from = _transform.GetMapCoordinates(args.User).Position;
        foreach (var target in args.HitEntities)
        {
            if (target != args.User && HasComp<MobStateComponent>(target))
                ApplyHit(target, ent.Comp.Hit, ent.Comp.Power, args.User, from);
        }
    }

    /// <summary>Conjured throwing weapons (spears): the hit lands when they strike someone.</summary>
    private void OnThrowHit(Entity<MagicOnHitComponent> ent, ref ThrowDoHitEvent args)
    {
        if (!HasComp<MobStateComponent>(args.Target))
            return;
        var thrower = args.Component.Thrower;
        ApplyHit(args.Target, ent.Comp.Hit, ent.Comp.Power, thrower, _transform.GetMapCoordinates(ent).Position);
        if (thrower is not { } t)
            return;
        if (ent.Comp.Special == MagicSpecial.TeleportCaster)
        {
            // Phase Dagger: you blink to whoever it hit.
            var leftFrom = Transform(t).Coordinates;
            _transform.SetCoordinates(t, Transform(args.Target).Coordinates);
            _transform.AttachToGridOrMap(t);
            _magic.TeleportMishap(t, leftFrom);
            QueueDel(ent);
            return;
        }
        DoSpecial(ent.Comp.Special, t, args.Target, Transform(args.Target).Coordinates, ent.Comp.Power, null);
    }

    /// <summary>Called by the magic system right after a spell projectile is fired: remember who cast it and how strongly.</summary>
    public void SetProjectileCaster(EntityUid projectile, EntityUid caster, float power)
    {
        if (!TryComp<MagicOnHitComponent>(projectile, out var hit))
            return;
        hit.Caster = caster;
        hit.Power = power;
    }

    private void OnConjuredDropped(Entity<MagicConjuredComponent> ent, ref DroppedEvent args)
    {
        if (ent.Comp.VanishOnDrop)
            QueueDel(ent);
    }

    #endregion

    #region Conditions

    public bool HasCondition(EntityUid uid, MagicCondition condition)
    {
        return TryComp<MagicConditionsComponent>(uid, out var comp) && comp.Active.ContainsKey(condition);
    }

    public void AddCondition(EntityUid uid, MagicCondition condition, float seconds, float power, EntityUid? source)
    {
        var comp = EnsureComp<MagicConditionsComponent>(uid);
        var ends = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        if (comp.Active.TryGetValue(condition, out var existing))
        {
            existing.Ends = ends > existing.Ends ? ends : existing.Ends;
            existing.Power = MathF.Max(existing.Power, power);
            existing.Source = source ?? existing.Source;
            return;
        }

        comp.Active[condition] = new MagicActiveCondition { Ends = ends, Power = power, Source = source, NextTick = _timing.CurTime };
        switch (condition)
        {
            case MagicCondition.Invisible:
                var stealth = EnsureComp<StealthComponent>(uid);
                _stealth.SetVisibility(uid, -0.5f, stealth);
                break;
            case MagicCondition.Telepathy:
                if (!HasComp<TelepathyComponent>(uid))
                {
                    AddComp<TelepathyComponent>(uid);
                    comp.Active[condition].Power = -1f; // remember we added it
                }
                if (!HasComp<MagicPermanentTelepathyComponent>(uid))
                    _magic.OpenMind(uid, permanent: false);
                break;
            case MagicCondition.Tongues:
                if (!HasComp<UniversalLanguageSpeakerComponent>(uid))
                {
                    AddComp<UniversalLanguageSpeakerComponent>(uid);
                    comp.Active[condition].Power = -1f;
                }
                break;
            case MagicCondition.Titan:
                _special.TryModifyTemporary(uid, SpecialStat.Endurance, 5, TimeSpan.FromSeconds(seconds), "magic-titan");
                _special.TryModifyTemporary(uid, SpecialStat.Luck, 3, TimeSpan.FromSeconds(seconds), "magic-titan");
                break;
            case MagicCondition.Weakness:
                _stun.TrySlowdown(uid, TimeSpan.FromSeconds(seconds), true, 0.4f, 0.4f); // really slow: 40% speed
                break;
        }
    }

    private void EndCondition(EntityUid uid, MagicConditionsComponent comp, MagicCondition condition)
    {
        if (!comp.Active.Remove(condition, out var active))
            return;
        switch (condition)
        {
            case MagicCondition.Invisible:
                RemComp<StealthComponent>(uid);
                break;
            case MagicCondition.Telepathy when active.Power < 0 && !HasComp<MagicPermanentTelepathyComponent>(uid):
                RemComp<TelepathyComponent>(uid);
                _magic.CloseMind(uid);
                break;
            case MagicCondition.Tongues when active.Power < 0:
                RemComp<UniversalLanguageSpeakerComponent>(uid);
                break;
        }
        if (condition is MagicCondition.Ward or MagicCondition.Absorb or MagicCondition.Hex)
            _magic.Notify(uid, Loc.GetString("magic-condition-faded"));
    }

    /// <summary>Uses one charge of a condition (Ward, Hex, Dodge). Returns true if it was there.</summary>
    public bool TryUseCondition(EntityUid uid, MagicCondition condition)
    {
        if (!TryComp<MagicConditionsComponent>(uid, out var comp) || !comp.Active.TryGetValue(condition, out var active))
            return false;
        active.Power -= 1f;
        if (active.Power <= 0f)
            EndCondition(uid, comp, condition);
        return true;
    }

    private void OnDamageModify(Entity<MagicConditionsComponent> ent, ref DamageModifyEvent args)
    {
        if (args.Damage.GetTotal() <= 0)
            return;

        if (ent.Comp.Active.ContainsKey(MagicCondition.Dodge))
        {
            EndCondition(ent, ent.Comp, MagicCondition.Dodge);
            args.Damage = new DamageSpecifier();
            _popup.PopupEntity(Loc.GetString("magic-dodged"), ent, PopupType.Medium);
            return;
        }

        if (ent.Comp.Active.ContainsKey(MagicCondition.Wet) && args.Damage.DamageDict.TryGetValue("Shock", out var shock) && shock > 0)
            args.Damage.DamageDict["Shock"] = shock * 2;

        if (ent.Comp.Active.TryGetValue(MagicCondition.Absorb, out var absorb))
        {
            var total = args.Damage.GetTotal().Float();
            var soaked = MathF.Min(total, absorb.Power);
            if (soaked > 0)
            {
                args.Damage *= (total - soaked) / total;
                absorb.Power -= soaked;
                if (absorb.Power <= 0)
                    EndCondition(ent, ent.Comp, MagicCondition.Absorb);
            }
        }

        if (!_bondTransfer && ent.Comp.Active.TryGetValue(MagicCondition.BloodBond, out var bond) && bond.Source is { } partner
            && partner != ent.Owner && !Deleted(partner) && !_mobState.IsDead(partner))
        {
            var half = args.Damage * 0.5f;
            args.Damage = half;
            _bondTransfer = true;
            _damageable.TryChangeDamage(partner, half, true);
            _bondTransfer = false;
        }
    }

    private void OnAttackAttempt(Entity<MagicConditionsComponent> ent, ref AttackAttemptEvent args)
    {
        if (args.Cancelled)
            return;
        if (TryUseCondition(ent, MagicCondition.Hex))
        {
            args.Cancel();
            _magic.Notify(ent, Loc.GetString("magic-hex-fail"));
            return;
        }
        if (ent.Comp.Active.ContainsKey(MagicCondition.Weakness) && _random.Prob(0.5f))
        {
            args.Cancel();
            _magic.Notify(ent, Loc.GetString("magic-weakness-fail"));
        }
    }

    #endregion

    #region Update

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        for (var i = _delayed.Count - 1; i >= 0; i--)
        {
            if (_delayed[i].When > now)
                continue;
            var run = _delayed[i].Run;
            _delayed.RemoveAt(i);
            // One broken delayed effect must never take the whole server down: log it and carry on.
            try
            {
                run();
            }
            catch (Exception e)
            {
                Log.Error($"A delayed magic effect failed: {e}");
            }
        }

        for (var i = _lightsOff.Count - 1; i >= 0; i--)
        {
            if (_lightsOff[i].When > now)
                continue;
            if (!Deleted(_lightsOff[i].Light))
                _lights.SetEnabled(_lightsOff[i].Light, true);
            _lightsOff.RemoveAt(i);
        }

        for (var i = _falseFaces.Count - 1; i >= 0; i--)
        {
            if (_falseFaces[i].When > now)
                continue;
            if (!Deleted(_falseFaces[i].Who))
                _meta.SetEntityName(_falseFaces[i].Who, _falseFaces[i].Name);
            _falseFaces.RemoveAt(i);
        }

        if (_shockStunnedUntil.Count > 64)
        {
            foreach (var key in _shockStunnedUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                _shockStunnedUntil.Remove(key);
        }

        UpdateAuras(now);
        UpdateConditions(now);
    }

    private void UpdateAuras(TimeSpan now)
    {
        var seconds = (float) now.TotalSeconds;
        var query = EntityQueryEnumerator<MagicAuraComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var aura, out var xform))
        {
            if (aura.NextPulse > seconds)
                continue;
            aura.NextPulse = seconds + aura.Interval;

            // The caster can be gone (gibbed, deleted) while their zone lingers: it keeps going as nobody's.
            if (aura.Caster is { } owner && TerminatingOrDeleted(owner))
                aura.Caster = null;

            if (aura.Drift > 0)
                _transform.SetCoordinates(uid, xform.Coordinates.Offset(_random.NextAngle().ToVec() * aura.Drift));

            var center = _transform.GetMapCoordinates(uid, xform);
            var targets = MobsNear(center, aura.Radius);
            if (aura.Items)
                targets.AddRange(ItemsNear(center, aura.Radius));
            foreach (var target in targets)
            {
                if (target == aura.Caster)
                {
                    if (aura.CasterHit != null)
                        ApplyHit(target, aura.CasterHit, aura.Power, aura.Caster);
                    else if (aura.HitCaster)
                        ApplyHit(target, aura.Hit, aura.Power, aura.Caster, center.Position);
                    continue;
                }
                if (HasComp<MagicServantComponent>(target) && CompOrNull<MagicServantComponent>(target)?.Master == aura.Caster)
                    continue;
                ApplyHit(target, aura.Hit, aura.Power, aura.Caster, center.Position);
            }

            if (aura.Special != MagicSpecial.None && aura.Caster is { } caster)
                DoSpecial(aura.Special, caster, null, xform.Coordinates, aura.Power, targets, aura.Radius);
        }
    }

    private void UpdateConditions(TimeSpan now)
    {
        var query = EntityQueryEnumerator<MagicConditionsComponent>();
        var ended = new List<MagicCondition>();
        while (query.MoveNext(out var uid, out var comp))
        {
            ended.Clear();
            foreach (var (condition, active) in comp.Active)
            {
                if (active.Ends <= now)
                {
                    ended.Add(condition);
                    continue;
                }
                if (active.NextTick > now)
                    continue;

                switch (condition)
                {
                    case MagicCondition.Leash when active.Source is { } master && !Deleted(master):
                        active.NextTick = now + TimeSpan.FromSeconds(0.5);
                        Throw(uid, _transform.GetMapCoordinates(master).Position, 1.5f, true, master);
                        break;
                    case MagicCondition.Tracked when active.Source is { } tracker && !Deleted(tracker):
                        active.NextTick = now + TimeSpan.FromSeconds(5);
                        var dir = _transform.GetMapCoordinates(uid).Position - _transform.GetMapCoordinates(tracker).Position;
                        _magic.Notify(tracker, Loc.GetString("magic-tracked", ("dir", DirectionName(dir)), ("meters", (int) dir.Length())));
                        break;
                    case MagicCondition.Nightmare:
                        active.NextTick = now + TimeSpan.FromSeconds(2);
                        if (HasComp<SleepingComponent>(uid) || HasComp<ForcedSleepingComponent>(uid))
                            _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Cellular"), 2), true);
                        break;
                    case MagicCondition.Titan:
                        // Faster healing: a little brute and burn mends every 2 seconds while the blessing lasts.
                        active.NextTick = now + TimeSpan.FromSeconds(2);
                        _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<Content.Shared.Damage.Prototypes.DamageGroupPrototype>("Brute"), -1.5f), true, false, canSever: false);
                        _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<Content.Shared.Damage.Prototypes.DamageGroupPrototype>("Burn"), -1f), true, false, canSever: false);
                        break;
                    case MagicCondition.Rot:
                        active.NextTick = now + TimeSpan.FromSeconds(2);
                        _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Cellular"), 1.5f), true);
                        break;
                    case MagicCondition.Wither:
                        active.NextTick = now + TimeSpan.FromSeconds(2);
                        _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Poison"), 2f), true);
                        break;
                    default:
                        active.NextTick = now + TimeSpan.FromSeconds(60);
                        break;
                }
            }
            foreach (var condition in ended)
                EndCondition(uid, comp, condition);
        }
    }

    private static string DirectionName(Vector2 dir)
    {
        if (dir.LengthSquared() < 1f)
            return "here";
        return new Angle(dir).GetDir().ToString();
    }

    #endregion

    #region Specials

    private bool IsDark(EntityCoordinates coords)
    {
        var map = _transform.ToMapCoordinates(coords);
        foreach (var light in _lookup.GetEntitiesInRange<PointLightComponent>(map, 6f))
        {
            if (!light.Comp.Enabled)
                continue;
            var distance = (_transform.GetMapCoordinates(light).Position - map.Position).Length();
            if (distance < light.Comp.Radius * 0.6f)
                return false;
        }
        return true;
    }

    private void MakeServant(Entity<MagicServantComponent> servant, EntityUid master)
    {
        servant.Comp.Master = master;
        _factions.IgnoreEntity(servant.Owner, master);

        // Shadow Clone copies the weapon in your hand: it hits as hard (and as fast) as that weapon does.
        // A gun gives its pistol-whip / rifle-butt melee damage (the clone can't shoot).
        if (MetaData(servant).EntityPrototype?.ID == "MagicShadowClone"
            && _hands.GetActiveItem(master) is { } held
            && TryComp<MeleeWeaponComponent>(held, out var weapon)
            && TryComp<MeleeWeaponComponent>(servant, out var claws))
        {
            claws.Damage = new DamageSpecifier(weapon.Damage);
            claws.AttackRate = weapon.AttackRate;
            Dirty(servant.Owner, claws);
        }
    }

    /// <summary>
    /// Possession: your mind goes into the target's body and theirs into yours, which collapses helpless.
    /// After 10 s (longer if Empowered); the trapped mind can't move or speak, both minds snap back. If a body is gone by then, that mind becomes a ghost.
    /// </summary>
    private void StartPossession(EntityUid caster, EntityUid victim, float power)
    {
        // A Mirror Image copy has no mind to take: possessing it would let you loot its illusion clothes.
        if (HasComp<MagicMirrorImageComponent>(victim))
        {
            _magic.Notify(caster, Loc.GetString("magic-possession-illusion"));
            return;
        }
        // One possession at a time per body: overlapping ones would send minds into the wrong bodies.
        if (HasComp<MagicPossessionComponent>(caster) || HasComp<MagicPossessionComponent>(victim))
        {
            _magic.Notify(caster, Loc.GetString("magic-possession-busy"));
            return;
        }
        if (!_mind.TryGetMind(caster, out var casterMind, out _))
            return;
        EnsureComp<MagicPossessionComponent>(caster);
        EnsureComp<MagicPossessionComponent>(victim);
        var victimHasMind = _mind.TryGetMind(victim, out var victimMind, out _);

        _mind.TransferTo(casterMind, null, createGhost: false);
        if (victimHasMind)
            _mind.TransferTo(victimMind, null, createGhost: false);
        _mind.TransferTo(casterMind, victim, ghostCheckOverride: true, createGhost: false);
        if (victimHasMind)
            _mind.TransferTo(victimMind, caster, ghostCheckOverride: true, createGhost: false);

        // 10 s (longer if Empowered). The victim's mind is stuck in your body, which can't move or speak:
        // only you move their body and talk with their mouth.
        var seconds = 10f * power;
        _stun.TryParalyze(caster, TimeSpan.FromSeconds(seconds), true);
        _status.TryAddStatusEffect<MutedComponent>(caster, "Muted", TimeSpan.FromSeconds(seconds), true);
        _popup.PopupEntity(Loc.GetString("magic-possession-start"), victim, victim, Content.Shared.Popups.PopupType.MediumCaution);
        _adminLog.Add(Content.Shared.Database.LogType.Action, Content.Shared.Database.LogImpact.High,
            $"{ToPrettyString(caster):player} possessed {ToPrettyString(victim):target} for {seconds} s");

        EntityUid? trapped = victimHasMind ? victimMind : null;
        _delayed.Add((_timing.CurTime + TimeSpan.FromSeconds(seconds), () => EndPossession(casterMind, caster, trapped, victim)));
    }

    private void EndPossession(EntityUid casterMind, EntityUid casterBody, EntityUid? victimMind, EntityUid victimBody)
    {
        var casterMindOk = !Deleted(casterMind);
        var victimMindOk = victimMind is { } vm && !Deleted(vm);
        if (casterMindOk)
            _mind.TransferTo(casterMind, null, createGhost: false);
        if (victimMindOk)
            _mind.TransferTo(victimMind!.Value, null, createGhost: false);

        if (casterMindOk)
            _mind.TransferTo(casterMind, Exists(casterBody) ? casterBody : null, ghostCheckOverride: true, createGhost: !Exists(casterBody));
        if (victimMindOk)
            _mind.TransferTo(victimMind!.Value, Exists(victimBody) ? victimBody : null, ghostCheckOverride: true, createGhost: !Exists(victimBody));

        if (Exists(casterBody))
        {
            RemComp<MagicPossessionComponent>(casterBody);
            _magic.Notify(casterBody, Loc.GetString("magic-possession-end"));
        }
        if (Exists(victimBody))
            RemComp<MagicPossessionComponent>(victimBody);
    }

    private void DoSpecial(MagicSpecial special, EntityUid caster, EntityUid? target, EntityCoordinates coords, float power, List<EntityUid>? targets, float radius = 3f)
    {
        switch (special)
        {
            case MagicSpecial.None:
            case MagicSpecial.TeleportCaster:
            case MagicSpecial.TeleportCasterDark:
                return;

            case MagicSpecial.SwapWithTarget when target is { } t:
            {
                var a = Transform(caster).Coordinates;
                var b = Transform(t).Coordinates;
                _transform.SetCoordinates(caster, b);
                _transform.SetCoordinates(t, a);
                _magic.TeleportMishap(caster, a);
                return;
            }

            case MagicSpecial.SwapTargetWithNearest when target is { } t:
            {
                var at = _transform.GetMapCoordinates(t);
                var other = MobsNear(at, 8f).Where(e => e != t && e != caster)
                    .OrderBy(e => (_transform.GetMapCoordinates(e).Position - at.Position).LengthSquared()).FirstOrDefault();
                if (!other.Valid)
                {
                    _magic.Notify(caster, Loc.GetString("magic-swap-nobody"));
                    return;
                }
                var a = Transform(t).Coordinates;
                var b = Transform(other).Coordinates;
                _transform.SetCoordinates(t, b);
                _transform.SetCoordinates(other, a);
                return;
            }

            case MagicSpecial.Shackles when target is { } t:
            {
                Disarm(t, false, caster);
                var cuffs = Spawn("MagicShacklesCuffs", Transform(t).Coordinates);
                if (!_cuffs.TryAddNewCuffs(t, caster, cuffs))
                    QueueDel(cuffs);
                return;
            }

            case MagicSpecial.ReadMind when target is { } t:
            {
                var job = _mind.TryGetMind(t, out var mindId, out _) ? _jobs.MindTryGetJobName(mindId) : Loc.GetString("magic-read-mind-nojob");
                var damage = TryComp<DamageableComponent>(t, out var dmg) ? dmg.TotalDamage.Int() : 0;
                var state = _mobState.IsDead(t) ? "dead" : _mobState.IsCritical(t) ? "dying" : damage > 50 ? "hurt" : damage > 10 ? "scratched" : "healthy";
                _magic.Notify(caster, Loc.GetString("magic-read-mind", ("name", Name(t)), ("job", job), ("state", state)), PopupType.Medium);
                return;
            }

            case MagicSpecial.Diagnose when target is { } t:
            {
                if (!TryComp<DamageableComponent>(t, out var dmg))
                    return;
                var parts = dmg.Damage.DamageDict.Where(p => p.Value > 0).OrderByDescending(p => p.Value)
                    .Select(p => $"{p.Key} {p.Value.Int()}").ToList();
                var bleeding = TryComp<Content.Server.Body.Components.BloodstreamComponent>(t, out var blood) && blood.BleedAmount > 0;
                var text = parts.Count == 0 ? Loc.GetString("magic-diagnose-healthy") : string.Join(", ", parts);
                if (bleeding)
                    text = string.Concat(text, ", ", Loc.GetString("magic-diagnose-bleeding"));
                _magic.Notify(caster, text, PopupType.Medium);
                return;
            }

            case MagicSpecial.SenseMinds:
            {
                var here = _transform.GetMapCoordinates(caster);
                var minds = MobsNear(here, 15f).Where(e => e != caster && _mind.TryGetMind(e, out _, out _)).ToList();
                if (minds.Count == 0)
                {
                    _magic.Notify(caster, Loc.GetString("magic-sense-none"));
                    return;
                }
                var dirs = minds.Select(m => DirectionName(_transform.GetMapCoordinates(m).Position - here.Position)).GroupBy(d => d)
                    .Select(g => $"{g.Count()} {g.Key}");
                _magic.Notify(caster, Loc.GetString("magic-sense-minds", ("count", minds.Count), ("where", string.Join(", ", dirs))), PopupType.Medium);
                return;
            }

            case MagicSpecial.Foresight:
            {
                var here = _transform.GetMapCoordinates(caster);
                foreach (var mob in MobsNear(here, 10f))
                {
                    if (mob == caster || !TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(mob, out var body))
                        continue;
                    var v = body.LinearVelocity;
                    var arrow = v.LengthSquared() < 0.1f ? "•" : new Angle(v).GetDir() switch
                    {
                        Direction.North => "↑", Direction.South => "↓", Direction.East => "→", Direction.West => "←",
                        Direction.NorthEast => "↗", Direction.NorthWest => "↖", Direction.SouthEast => "↘", _ => "↙",
                    };
                    _popup.PopupEntity(arrow, mob, caster, PopupType.Large);
                }
                return;
            }

            case MagicSpecial.LightsOut:
            {
                var at = _transform.ToMapCoordinates(coords);
                foreach (var light in _lookup.GetEntitiesInRange<PointLightComponent>(at, MathF.Max(radius, 0.5f)))
                {
                    if (!light.Comp.Enabled || HasComp<MobStateComponent>(light))
                        continue;
                    _lights.SetEnabled(light, false);
                    _lightsOff.Add((_timing.CurTime + TimeSpan.FromSeconds(20 * power), light));
                }
                return;
            }

            case MagicSpecial.Emp:
                _emp.EmpPulse(_transform.ToMapCoordinates(coords), radius, 50000f * power, 10f * power);
                return;

            case MagicSpecial.Possession when target is { } victim && victim != caster:
                StartPossession(caster, victim, power);
                return;

            case MagicSpecial.LightsOutEmp:
                DoSpecial(MagicSpecial.LightsOut, caster, target, coords, power, targets, radius);
                DoSpecial(MagicSpecial.Emp, caster, target, coords, power, targets, radius);
                return;

            case MagicSpecial.Scatter:
            {
                var points = new List<EntityCoordinates>();
                var mapId = _transform.GetMapId(coords);
                var here = _transform.ToMapCoordinates(coords).Position;
                var spawnQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
                while (spawnQuery.MoveNext(out _, out _, out var xform))
                {
                    // Only spots around the area (within 40 tiles), never across the map into someone's locked base.
                    if (xform.MapID == mapId
                        && (_transform.GetWorldPosition(xform) - here).LengthSquared() <= 40f * 40f)
                        points.Add(xform.Coordinates);
                }
                foreach (var victim in targets ?? new List<EntityUid>())
                {
                    if (victim == caster || !HasComp<MobStateComponent>(victim))
                        continue;
                    var to = points.Count > 0 ? _random.Pick(points) : Transform(victim).Coordinates.Offset(_random.NextVector2(15f, 30f));
                    _transform.SetCoordinates(victim, to);
                    _transform.AttachToGridOrMap(victim);
                    _stun.TryParalyze(victim, TimeSpan.FromSeconds(6 * power), true);
                }
                return;
            }

            case MagicSpecial.Unbind:
            {
                var query = EntityQueryEnumerator<MagicGrimoireComponent>();
                var freed = 0;
                while (query.MoveNext(out var book, out var grimoire))
                {
                    if (grimoire.MagicOwner != caster)
                        continue;
                    for (var i = 0; i < grimoire.MagicPages.Count; i++)
                    {
                        if (grimoire.MagicPages[i].Readied)
                            _grimoire.SetReadied((book, grimoire), i, false);
                    }
                    grimoire.MagicOwner = null;
                    Dirty(book, grimoire);
                    freed++;
                }
                _magic.Notify(caster, Loc.GetString(freed > 0 ? "magic-unbind-done" : "magic-unbind-none"));
                return;
            }

            case MagicSpecial.Resurrect when target is { } t:
            {
                if (!_mobState.IsDead(t))
                    return;
                if (_thresholds.TryGetThresholdForState(t, MobState.Critical, out var crit)
                    && TryComp<DamageableComponent>(t, out var dmg) && dmg.TotalDamage >= crit)
                {
                    _magic.Notify(caster, Loc.GetString("magic-resurrect-too-hurt"));
                    return;
                }
                _mobState.ChangeMobState(t, MobState.Alive);
                EntityManager.System<MagicSoulSystem>().MendLimbs(t);   // most limb damage off, broken legs set
                EntityManager.System<Content.Server._ReclamationProject.Medical.BioMonitor.BioMonitorSystem>().RemoveAllFragments(t);
                _popup.PopupEntity(Loc.GetString("magic-resurrect-done"), t, PopupType.Large);
                // Like a defibrillator: if their ghost is wandering, ask it to come back into the body.
                if (_mind.TryGetMind(t, out _, out var revivedMind) && revivedMind.Session is { } revivedSession
                    && revivedMind.CurrentEntity != t)
                    _eui.OpenEui(new Content.Server.Ghost.ReturnToBodyEui(revivedMind, _mind), revivedSession);
                _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(caster):player} resurrected {ToPrettyString(t):target} with magic");
                return;
            }

            case MagicSpecial.DispelArea:
                foreach (var victim in targets ?? new List<EntityUid>())
                {
                    if (victim != caster)
                        _magic.Dispel(victim, 5f);
                }
                return;

            case MagicSpecial.ImplantThought when target is { } t:
                _chat.TrySendInGameICMessage(t, Loc.GetString(_random.Pick(ImplantLines)), InGameICChatType.Speak, false);
                return;

            case MagicSpecial.MemoryWipe when target is { } t:
                _magic.Notify(t, Loc.GetString("magic-memory-wipe"), PopupType.LargeCaution);
                return;

            case MagicSpecial.Psychometry when target is { } t:
            {
                if (!TryComp<ForensicsComponent>(t, out var prints) || prints.Fingerprints.Count == 0)
                {
                    _magic.Notify(caster, Loc.GetString("magic-psychometry-none"));
                    return;
                }
                _magic.Notify(caster, Loc.GetString("magic-psychometry", ("prints", string.Join(", ", prints.Fingerprints.Take(3)))), PopupType.Medium);
                return;
            }

            case MagicSpecial.FalseFace when target is { } t:
            {
                if (_falseFaces.Any(f => f.Who == caster))
                    return;
                _falseFaces.Add((_timing.CurTime + TimeSpan.FromSeconds(60 * power), caster, Name(caster)));
                _meta.SetEntityName(caster, Name(t));
                return;
            }

            case MagicSpecial.MindInscription:
            {
                var book = _hands.EnumerateHeld(caster).FirstOrDefault(HasComp<MagicGrimoireComponent>);
                if (!book.Valid || !TryComp<MagicGrimoireComponent>(book, out var grimoire) || grimoire.MagicOwner != caster)
                {
                    _magic.Notify(caster, Loc.GetString("magic-inscription-need-book"));
                    return;
                }
                var learned = 0;
                foreach (var page in grimoire.MagicPages)
                {
                    if (page.Spell is not { } spell || !_magic.LearnSpell(caster, spell, ignoreLimit: true))
                        continue;
                    learned++;
                    if (!string.IsNullOrWhiteSpace(page.Note) && TryComp<MagicCasterComponent>(caster, out var mindCaster))
                        mindCaster.MagicKnownSpellNotes[spell] = page.Note;
                }
                QueueDel(book);
                _magic.Notify(caster, Loc.GetString("magic-inscription-done", ("count", learned)), PopupType.Large);
                return;
            }

            case MagicSpecial.TransmuteMetal:
                Transmute(caster, new Dictionary<string, string> { ["MaterialScrap"] = "SheetSteel", ["SheetSteel"] = "SheetPlasteel" });
                return;
            case MagicSpecial.TransmuteRare:
            {
                var pick = _random.Pick(new[] { "IngotSilver", "IngotGold", "MaterialUranium" });
                Transmute(caster, new Dictionary<string, string> { ["SheetSteel"] = pick });
                return;
            }
            case MagicSpecial.TransmuteLife:
                TransmuteFood(caster);
                return;
            case MagicSpecial.Duplicate:
                Duplicate(caster);
                return;
            case MagicSpecial.ArcaneForge:
                Forge(caster);
                return;

            case MagicSpecial.TelekinesisGrab when target is { } grabbed:
            {
                if (HasComp<ItemComponent>(grabbed) && !_inventoryOrHeld(grabbed))
                {
                    _hands.TryPickupAnyHand(caster, grabbed);
                    return;
                }

                if (grabbed == caster)
                    return;
                // Someone in power armour: the fusion core is torn out of the suit and flies into your hand.
                if (_fusionCore.YankCoreFromWearer(grabbed, caster) is { } core)
                {
                    _hands.TryPickupAnyHand(caster, core);
                    _magic.Notify(grabbed, Loc.GetString("magic-telekinesis-core-yanked"), Content.Shared.Popups.PopupType.LargeCaution);
                    _adminLog.Add(Content.Shared.Database.LogType.Action, Content.Shared.Database.LogImpact.Medium,
                        $"{ToPrettyString(caster):player} tore the fusion core out of {ToPrettyString(grabbed):target}'s power armour with Telekinesis");
                    return;
                }
                // Otherwise whatever is in their active hand (or any hand) flies into yours.
                var stolen = _hands.GetActiveItem(grabbed) ?? HeldOther(grabbed);
                if (stolen is not { } item || !_hands.TryDrop(grabbed, item, checkActionBlocker: false))
                    return;
                _hands.TryPickupAnyHand(caster, item);
                _popup.PopupEntity(Loc.GetString("magic-telekinesis-stolen", ("item", item)), grabbed, grabbed, Content.Shared.Popups.PopupType.MediumCaution);
                _adminLog.Add(Content.Shared.Database.LogType.Action, Content.Shared.Database.LogImpact.Medium,
                    $"{ToPrettyString(caster):player} stole {ToPrettyString(item):item} from {ToPrettyString(grabbed):target} with Telekinesis");
                return;
            }

            case MagicSpecial.FetchItem when target is { } t:
                if (HasComp<ItemComponent>(t) && !_inventoryOrHeld(t))
                    _hands.TryPickupAnyHand(caster, t);
                return;

            case MagicSpecial.MirrorImage:
            case MagicSpecial.Servant:
                return; // handled by the spawned entities
        }
    }

    private EntityUid? HeldOther(EntityUid caster)
    {
        foreach (var held in _hands.EnumerateHeld(caster))
            return held;
        return null;
    }

    private void Transmute(EntityUid caster, Dictionary<string, string> map)
    {
        foreach (var held in _hands.EnumerateHeld(caster).ToList())
        {
            var proto = MetaData(held).EntityPrototype;
            if (proto == null)
                continue;
            var key = map.Keys.FirstOrDefault(k => proto.ID.StartsWith(k, StringComparison.Ordinal));
            if (key == null)
                continue;
            var count = TryComp<StackComponent>(held, out var stack) ? stack.Count : 1;
            var coords = Transform(caster).Coordinates;
            QueueDel(held);
            var made = Spawn(map[key], coords);
            if (TryComp<StackComponent>(made, out var newStack))
                _stacks.SetCount(made, Math.Max(1, count / 2), newStack);
            _hands.TryPickupAnyHand(caster, made);
            _magic.Notify(caster, Loc.GetString("magic-transmute-done"), PopupType.Medium);
            return;
        }
        _magic.Notify(caster, Loc.GetString("magic-transmute-nothing"));
    }

    private void TransmuteFood(EntityUid caster)
    {
        foreach (var held in _hands.EnumerateHeld(caster).ToList())
        {
            if (!HasComp<Content.Server.Nutrition.Components.FoodComponent>(held))
                continue;
            QueueDel(held);
            var made = Spawn(_random.Pick(new[] { "Brutepack", "Ointment", "Bloodpack" }), Transform(caster).Coordinates);
            _hands.TryPickupAnyHand(caster, made);
            _magic.Notify(caster, Loc.GetString("magic-transmute-done"), PopupType.Medium);
            return;
        }
        _magic.Notify(caster, Loc.GetString("magic-transmute-nothing"));
    }

    private void Duplicate(EntityUid caster)
    {
        foreach (var held in _hands.EnumerateHeld(caster).ToList())
        {
            if (HasComp<GunComponent>(held) || HasComp<StackComponent>(held) || HasComp<MagicGrimoireComponent>(held)
                || HasComp<MagicConjuredComponent>(held) || HasComp<Content.Shared.Storage.StorageComponent>(held)
                || HasComp<MagicCodexLeafComponent>(held) || HasComp<ProjectileComponent>(held)
                || HasComp<Content.Shared.Weapons.Ranged.Components.CartridgeAmmoComponent>(held)
                || MetaData(held).EntityPrototype is not { } proto)
                continue;
            var copy = Spawn(proto.ID, Transform(caster).Coordinates);
            _hands.TryPickupAnyHand(caster, copy);
            _magic.Notify(caster, Loc.GetString("magic-duplicate-done"), PopupType.Medium);
            return;
        }
        _magic.Notify(caster, Loc.GetString("magic-duplicate-nothing"));
    }

    private void Forge(EntityUid caster)
    {
        foreach (var held in _hands.EnumerateHeld(caster).ToList())
        {
            if (!TryComp<MeleeWeaponComponent>(held, out var melee) || HasComp<MagicForgedComponent>(held))
                continue;
            melee.Damage *= 1.25f;
            Dirty(held, melee);
            AddComp<MagicForgedComponent>(held);
            _meta.SetEntityName(held, Loc.GetString("magic-forged-name", ("name", Name(held))));
            _magic.Notify(caster, Loc.GetString("magic-forge-done"), PopupType.Medium);
            return;
        }
        _magic.Notify(caster, Loc.GetString("magic-forge-nothing"));
    }

    #endregion
}
