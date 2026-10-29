using Content.Server._Misfits.Warcry;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._ReclamationProject.Charisma;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Charisma;

/// <summary>Grants the Charisma capstone while reusing Legion's warcry targeting and overlay.</summary>
public sealed class CharismaRallySystem : EntitySystem
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(3);
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly WarcrySystem _warcry = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<CharismaRallyComponent, CharismaRallyActionEvent>(OnActivate);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args) => RefreshAction(args.ChangedEntity);
    private void OnStatsReady(ref SpecialStatsReadyEvent args) => RefreshAction(args.Entity);
    private void OnSpecialShutdown(ref SpecialShutdownEvent args) => RemoveAction(args.Entity);

    private void RefreshAction(EntityUid uid)
    {
        if (_special.GetEffective(uid, SpecialStat.Charisma) < 10)
        {
            RemoveAction(uid);
            return;
        }
        var ability = EnsureComp<CharismaRallyComponent>(uid);
        if (ability.Action != null && !Deleted(ability.Action.Value))
            return;
        _actions.AddAction(uid, ref ability.Action, "ActionCharismaRally");
        if (ability.NextUseAt > _timing.CurTime)
            _actions.SetCooldown(ability.Action, ability.NextUseAt - Cooldown, ability.NextUseAt);
        Dirty(uid, ability);
    }

    private void RemoveAction(EntityUid uid)
    {
        if (!TryComp<CharismaRallyComponent>(uid, out var ability) || ability.Action == null)
            return;
        _actions.RemoveAction(uid, ability.Action);
        if (!Deleted(ability.Action.Value))
            QueueDel(ability.Action.Value);
        ability.Action = null;
        // Preserve cooldown through temporary SPECIAL changes.
        Dirty(uid, ability);
    }

    private void OnActivate(Entity<CharismaRallyComponent> ent, ref CharismaRallyActionEvent args)
    {
        var now = _timing.CurTime;
        if (args.Handled || args.Performer != ent.Owner || !HasComp<ActorComponent>(ent) ||
            _special.GetEffective(ent, SpecialStat.Charisma) < 10 || !_mob.IsAlive(ent) ||
            !_blocker.CanInteract(ent, null) || ent.Comp.NextUseAt > now)
            return;

        _warcry.ActivateRally(ent, includeSelf: false);
        ent.Comp.NextUseAt = now + Cooldown;
        _actions.SetCooldown(ent.Comp.Action, now, ent.Comp.NextUseAt);
        Dirty(ent);
        args.Handled = true;
    }
}
