using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._ReclamationProject.Luck;
using Content.Shared.Actions;
using Robust.Shared.Timing;
using System.Numerics;
using System.Linq;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._ReclamationProject.Luck;

public sealed class ScroungersFortuneSystem : EntitySystem
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    [Dependency] private readonly Robust.Shared.Containers.SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    public const float SearchSeconds = 5f;

    public override void Initialize()
    {
        SubscribeLocalEvent<ScroungersFortuneComponent, ScroungersFortuneActionEvent>(OnActivate);
        SubscribeLocalEvent<ScroungersFortuneComponent, ScroungersFortuneDoAfterEvent>(OnFinished);
        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args) => RefreshAction(args.ChangedEntity);
    private void OnStatsReady(ref SpecialStatsReadyEvent args) => RefreshAction(args.Entity);
    private void OnSpecialShutdown(ref SpecialShutdownEvent args) => RemoveAction(args.Entity);

    private void RefreshAction(EntityUid uid)
    {
        if (_special.GetEffective(uid, SpecialStat.Luck) < 10)
        {
            RemoveAction(uid);
            return;
        }
        var ability = EnsureComp<ScroungersFortuneComponent>(uid);
        if (ability.Action != null && !Deleted(ability.Action.Value))
            return;
        _actions.AddAction(uid, ref ability.Action, "ActionScroungersFortune");
        if (ability.NextUseAt > _timing.CurTime)
            _actions.SetCooldown(ability.Action, ability.NextUseAt - Cooldown, ability.NextUseAt);
        Dirty(uid, ability);
    }

    private void RemoveAction(EntityUid uid)
    {
        if (!TryComp<ScroungersFortuneComponent>(uid, out var ability) || ability.Action == null)
            return;
        CancelSearch(uid, ability);
        _actions.RemoveAction(uid, ability.Action);
        if (!Deleted(ability.Action.Value))
            QueueDel(ability.Action.Value);
        ability.Action = null;
        Dirty(uid, ability);
    }

    private void CancelSearch(EntityUid uid, ScroungersFortuneComponent ability)
    {
        _doAfter.Cancel(ability.DoAfter);
        ability.DoAfter = null;
        ability.Searching = false;
        Dirty(uid, ability);
    }

    private void OnActivate(Entity<ScroungersFortuneComponent> ent, ref ScroungersFortuneActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || ent.Comp.Searching ||
            ent.Comp.NextUseAt > _timing.CurTime || _special.GetEffective(ent, SpecialStat.Luck) < 10 ||
            !_mob.IsAlive(ent) || !_blocker.CanInteract(ent, null) || _containers.IsEntityInContainer(ent))
            return;
        var search = new DoAfterArgs(EntityManager, ent, SearchSeconds, new ScroungersFortuneDoAfterEvent(), ent)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            BlockDuplicate = true,
            CancelDuplicate = false,
        };
        if (!_doAfter.TryStartDoAfter(search, out var id))
            return;
        ent.Comp.DoAfter = id;
        ent.Comp.Searching = true;
        Dirty(ent);
        args.Handled = true;
    }

    private void OnFinished(Entity<ScroungersFortuneComponent> ent, ref ScroungersFortuneDoAfterEvent args)
    {
        if (args.Handled || ent.Comp.DoAfter is not { } id || id.Index != args.DoAfter.Index)
            return;
        args.Handled = true;
        ent.Comp.DoAfter = null;
        ent.Comp.Searching = false;
        Dirty(ent);
        if (args.Cancelled || _special.GetEffective(ent, SpecialStat.Luck) < 10 || !_mob.IsAlive(ent) ||
            !_blocker.CanInteract(ent, null) || _containers.IsEntityInContainer(ent))
            return;
        if (!TryFindSpot(ent, out var coordinates))
        {
            _popup.PopupEntity(Loc.GetString("scroungers-fortune-no-room"), ent, ent);
            return;
        }
        var pile = SelectPile(_random.Next(100));
        Spawn(pile, coordinates);
        ent.Comp.NextUseAt = _timing.CurTime + Cooldown;
        _actions.SetCooldown(ent.Comp.Action, _timing.CurTime, ent.Comp.NextUseAt);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("scroungers-fortune-found"), ent, ent);
    }

    public static string SelectPile(int roll) => roll switch
    {
        >= 0 and < 50 => "ReclamationOldScrap",
        >= 50 and < 85 => "ReclamationOverlookedScrap",
        >= 85 and < 95 => "ReclamationEasyToMissPile",
        >= 95 and < 100 => "ReclamationSomethingShiny",
        _ => throw new ArgumentOutOfRangeException(nameof(roll)),
    };

    public bool TryFindSpot(EntityUid user, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        var origin = _transform.GetMapCoordinates(user);
        if (!_map.TryFindGridAt(origin, out var gridUid, out var grid))
            return false;
        var center = _map.CoordinatesToTile(gridUid, grid, origin);
        var candidates = new System.Collections.Generic.List<EntityCoordinates>();
        for (var x = -4; x <= 4; x++)
        for (var y = -4; y <= 4; y++)
        {
            var tile = _map.GetTileRef(gridUid, grid, center + new Vector2i(x, y));
            if (tile.Tile.IsSpace() || _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
                continue;
            var candidate = _map.GridTileToLocal(gridUid, grid, tile.GridIndices);
            if (Vector2.DistanceSquared(origin.Position, _transform.ToMapCoordinates(candidate).Position) > 16f ||
                !_interaction.InRangeUnobstructed(user, candidate, ScroungersFortuneComponent.Radius))
                continue;
            candidates.Add(candidate);
        }
        if (candidates.Count == 0)
            return false;
        coordinates = _random.Pick(candidates);
        return true;
    }
}
