using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._ReclamationProject.Agility;
using Content.Shared.Actions;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Agility;

public sealed class AgilityQuickstepSystem : EntitySystem
{
    public static readonly TimeSpan Cooldown = AgilityQuickstepMovementSystem.Cooldown;
    public const float Range = AgilityQuickstepMovementSystem.Range;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args) => RefreshAction(args.ChangedEntity);
    private void OnStatsReady(ref SpecialStatsReadyEvent args) => RefreshAction(args.Entity);
    private void OnSpecialShutdown(ref SpecialShutdownEvent args) => RemoveAction(args.Entity);

    private void RefreshAction(EntityUid uid)
    {
        if (_special.GetEffective(uid, SpecialStat.Agility) < 10)
        {
            RemoveAction(uid);
            return;
        }
        var ability = EnsureComp<AgilityQuickstepComponent>(uid);
        if (ability.Action != null && !Deleted(ability.Action.Value))
            return;
        _actions.AddAction(uid, ref ability.Action, "ActionAgilityQuickstep");
        if (ability.NextUseAt > _timing.CurTime)
            _actions.SetCooldown(ability.Action, ability.NextUseAt - Cooldown, ability.NextUseAt);
        Dirty(uid, ability);
    }

    private void RemoveAction(EntityUid uid)
    {
        if (!TryComp<AgilityQuickstepComponent>(uid, out var ability) || ability.Action == null)
            return;
        _actions.RemoveAction(uid, ability.Action);
        if (!Deleted(ability.Action.Value))
            QueueDel(ability.Action.Value);
        ability.Action = null;
        Dirty(uid, ability);
    }

}
