using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - typed casting: saying a spell's words in normal chat casts it.
/// Rules: you need a grimoire bound to you, the spell must be readied (or held in your mind),
/// and you must have cast it the normal way at least once this round. Typed casting skips nothing
/// else and costs 25% more mana. Aimed spells fly the way you're facing; touch spells hit yourself.
/// A word 1-2 letters off misfires: fizzle, backfire, wrong spell or a wild surge.
/// </summary>
public sealed class MagicChatCastSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly MagicWordsSystem _words = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly Content.Shared.Interaction.SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly Content.Shared.Whitelist.EntityWhitelistSystem _whitelist = default!;

    private const float AimDistance = 6f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicCasterComponent, EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(Entity<MagicCasterComponent> ent, ref EntitySpokeEvent args)
    {
        // The words a caster says aloud while casting (SpeakIncantation) must not cast the spell a second time.
        if (_magic.SpeakingIncantation || args.Channel != null || !_magic.KnowsArcane(ent) || !HasBoundGrimoire(ent))
            return;

        if (!_words.TryResolveFuzzy(args.Message, out var spellId, out var exact))
            return; // just normal talking

        if (!TryGetSpellAction(ent, spellId, out var action))
            return; // a spell they don't have ready: nothing happens

        // Dying words: Undying Will whispered as your Last Words in crit brings you back instead of dying.
        if (spellId == "UndyingWill" && EntityManager.System<MagicSoulSystem>().TryDyingWords(ent))
            return;

        if (!ent.Comp.CastThisRound.Contains(spellId))
        {
            _magic.Notify(ent, Loc.GetString("magic-typed-not-yet"));
            return;
        }

        if (!_actions.TryGetActionData(action, out var data) || data.Cooldown is { } cd && cd.End > _timing.CurTime)
            return;

        if (!exact && !ent.Comp.MagicNoMisfire)
        {
            Misfire(ent, action, spellId);
            return;
        }

        Cast(ent, action);
    }

    private bool HasBoundGrimoire(EntityUid uid)
    {
        var query = EntityQueryEnumerator<MagicGrimoireComponent>();
        while (query.MoveNext(out _, out var book))
        {
            if (book.MagicOwner == uid)
                return true;
        }
        return false;
    }

    /// <summary>Finds the action entity for a spell the caster has ready (readied page or in their mind).</summary>
    public bool TryGetSpellAction(Entity<MagicCasterComponent> ent, ProtoId<MagicSpellPrototype> spellId, out EntityUid action)
    {
        if (ent.Comp.KnownSpellActions.TryGetValue(spellId, out action))
            return true;

        var query = EntityQueryEnumerator<MagicGrimoireComponent>();
        while (query.MoveNext(out _, out var book))
        {
            if (book.MagicOwner != ent.Owner)
                continue;
            foreach (var (index, pageAction) in book.PageActions)
            {
                if (book.MagicPages[index].Spell == spellId)
                {
                    action = pageAction;
                    return true;
                }
            }
        }

        var torn = EntityQueryEnumerator<MagicTornPageComponent>();
        while (torn.MoveNext(out _, out var page))
        {
            if (page.MagicOwner == ent.Owner && page.Page.Spell == spellId && page.Action is { } tornAction
                && _actions.TryGetActionData(tornAction, out var data) && data.AttachedEntity == ent.Owner)
            {
                action = tornAction;
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>Casts a spell's action without the hotbar: aimed spells go the way you face, touch spells hit you.</summary>
    public bool Cast(EntityUid user, EntityUid action)
    {
        if (!TryComp<ActionsComponent>(user, out var actions) || !_actions.TryGetActionData(action, out var data))
            return false;

        // Typed casting skips the reading time, except for King spells.
        MagicCasterComponent? caster = null;
        if (TryComp<MagicSpellActionComponent>(action, out var spellAction) && spellAction.Spell is { } spellId
            && _proto.TryIndex(spellId, out var spell) && spell.Rank != MagicRank.King
            && TryComp(user, out caster))
        {
            caster.ReadyToFire = action;
        }

        _magic.TypedCastInProgress = true;
        try
        {
            switch (data)
            {
                case WorldTargetActionComponent world when world.Event != null:
                    world.Event.Target = AimPoint(user);
                    _actions.PerformAction(user, actions, action, world, world.Event, _timing.CurTime, predicted: false);
                    break;
                case EntityTargetActionComponent entity when entity.Event != null:
                    // Aimed at whoever you are facing (nearest valid target in front of you, in range and sight).
                    // Only spells that may target yourself (heals and the like) fall back to you.
                    if (PickFacingTarget(user, entity) is not { } picked)
                    {
                        _magic.Notify(user, Loc.GetString("magic-typed-no-target"));
                        return false;
                    }
                    entity.Event.Target = picked;
                    _actions.PerformAction(user, actions, action, entity, entity.Event, _timing.CurTime, predicted: false);
                    break;
                case InstantActionComponent instant when instant.Event != null:
                    _actions.PerformAction(user, actions, action, instant, instant.Event, _timing.CurTime, predicted: false);
                    break;
                default:
                    return false;
            }
        }
        finally
        {
            _magic.TypedCastInProgress = false;
            if (caster != null && caster.ReadyToFire == action)
                caster.ReadyToFire = null;
        }

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):player} cast a spell by typing its words");
        return true;
    }

    /// <summary>The valid target closest to where the caster faces (within 35 degrees), or the caster if allowed.</summary>
    private EntityUid? PickFacingTarget(EntityUid user, EntityTargetActionComponent action)
    {
        var xform = Transform(user);
        var origin = _transform.GetMapCoordinates(xform);
        var facing = _transform.GetWorldRotation(xform).ToWorldVec();
        EntityUid? best = null;
        var bestScore = float.MaxValue;

        foreach (var other in _lookup.GetEntitiesInRange(origin, action.Range, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (other == user)
                continue;
            if (action.Whitelist != null && !_whitelist.IsValid(action.Whitelist, other))
                continue;
            if (action.Blacklist != null && _whitelist.IsValid(action.Blacklist, other))
                continue;

            var to = _transform.GetMapCoordinates(other).Position - origin.Position;
            var dist = to.Length();
            if (dist < 0.01f)
                continue;
            var angle = MathF.Acos(Math.Clamp(Vector2.Dot(to / dist, facing), -1f, 1f));
            if (angle > MathF.PI * 35f / 180f)
                continue;
            if (!_interaction.InRangeUnobstructed(user, other, action.Range, popup: false))
                continue;

            var score = dist + angle * 2f; // prefer close and straight ahead
            if (score < bestScore)
            {
                bestScore = score;
                best = other;
            }
        }

        return best ?? (action.CanTargetSelf ? user : null);
    }

    private EntityCoordinates AimPoint(EntityUid user)
    {
        var xform = Transform(user);
        var dir = _transform.GetWorldRotation(xform).ToWorldVec();
        return xform.Coordinates.Offset(dir * AimDistance);
    }

    private void Misfire(Entity<MagicCasterComponent> ent, EntityUid action, ProtoId<MagicSpellPrototype> spellId)
    {
        var spell = _proto.Index(spellId);
        var cost = spell.ManaCost * ent.Comp.TypedManaMultiplier;
        _magic.PayMana(ent, cost);

        // Bigger spells misfire harder.
        var hard = (spell.Words.Count - 1) * 0.05f;
        var roll = _random.NextFloat();
        string result;

        if (roll < 0.40f - hard)
        {
            result = "fizzle";
            _magic.Notify(ent, Loc.GetString("magic-misfire-fizzle"));
        }
        else if (roll < 0.65f)
        {
            result = "backfire";
            var damage = new DamageSpecifier(_proto.Index<DamageTypePrototype>("Heat"), 10 + spell.ManaCost * 0.5f);
            _damage.TryChangeDamage(ent, damage, interruptsDoAfters: true);
            _magic.Notify(ent, Loc.GetString("magic-misfire-backfire"), PopupType.MediumCaution);
        }
        else if (roll < 0.85f - hard)
        {
            result = "wrong spell";
            var others = ent.Comp.MagicKnownSpells.Concat(ReadiedSpells(ent)).Where(s => s != spellId).Distinct().ToList();
            if (others.Count > 0 && TryGetSpellAction(ent, _random.Pick(others), out var other))
                Cast(ent, other);
            else
                _magic.Notify(ent, Loc.GetString("magic-misfire-fizzle"));
            _magic.Notify(ent, Loc.GetString("magic-misfire-wrong"));
        }
        else
        {
            result = "wild surge";
            WildSurge(ent);
        }

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(ent.Owner):player} misspelled a typed spell ({spellId}): {result}");
    }

    private IEnumerable<ProtoId<MagicSpellPrototype>> ReadiedSpells(EntityUid uid)
    {
        var query = EntityQueryEnumerator<MagicGrimoireComponent>();
        while (query.MoveNext(out _, out var book))
        {
            if (book.MagicOwner != uid)
                continue;
            foreach (var page in book.MagicPages)
            {
                if (page.Readied && page.Spell is { } spell)
                    yield return spell;
            }
        }
    }

    private void WildSurge(EntityUid uid)
    {
        switch (_random.Next(4))
        {
            case 0:
                _flammable.AdjustFireStacks(uid, 2f);
                _flammable.Ignite(uid, uid);
                _magic.Notify(uid, Loc.GetString("magic-surge-fire"), PopupType.MediumCaution);
                break;
            case 1:
                var xform = Transform(uid);
                var offset = new Vector2(_random.NextFloat(-4f, 4f), _random.NextFloat(-4f, 4f));
                _transform.SetCoordinates(uid, xform.Coordinates.Offset(offset));
                _transform.AttachToGridOrMap(uid);
                _magic.Notify(uid, Loc.GetString("magic-surge-teleport"));
                break;
            case 2:
                _stun.TryKnockdown(uid, TimeSpan.FromSeconds(3), true);
                _magic.Notify(uid, Loc.GetString("magic-surge-knockdown"), PopupType.MediumCaution);
                break;
            default:
                _magic.RestoreMana(uid, 15f);
                _magic.Notify(uid, Loc.GetString("magic-surge-lucky"));
                break;
        }
    }
}
