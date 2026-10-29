using System.Linq;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Power.Components;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._ReclamationProject.Intelligence;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Timing;
using Robust.Shared.Player;

namespace Content.Server._ReclamationProject.Intelligence;

public sealed class IntelligenceOverrideSystem : EntitySystem
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly EmagSystem _emag = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly NPCRetaliationSystem _retaliation = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;

    public override void Initialize()
    {
        SubscribeNetworkEvent<OverrideVisualSelectionEvent>(OnVisualSelection);
        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<IntelligenceOverrideComponent, IntelligenceOverrideActionEvent>(OnActivate);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args) => RefreshAction(args.ChangedEntity);
    private void OnStatsReady(ref SpecialStatsReadyEvent args) => RefreshAction(args.Entity);
    private void OnSpecialShutdown(ref SpecialShutdownEvent args) => RemoveAction(args.Entity);

    private void RefreshAction(EntityUid uid)
    {
        if (_special.GetEffective(uid, SpecialStat.Intelligence) < 10)
        {
            RemoveAction(uid);
            return;
        }
        var ability = EnsureComp<IntelligenceOverrideComponent>(uid);
        if (ability.Action != null && !Deleted(ability.Action.Value))
            return;
        _actions.AddAction(uid, ref ability.Action, "ActionIntelligenceOverride");
        if (ability.NextUseAt > _timing.CurTime)
            _actions.SetCooldown(ability.Action, ability.NextUseAt - Cooldown, ability.NextUseAt);
        Dirty(uid, ability);
    }

    private void RemoveAction(EntityUid uid)
    {
        if (!TryComp<IntelligenceOverrideComponent>(uid, out var ability) || ability.Action == null)
            return;
        _actions.RemoveAction(uid, ability.Action);
        if (!Deleted(ability.Action.Value))
            QueueDel(ability.Action.Value);
        ability.Action = null;
        ability.Selected = false;
        Dirty(uid, ability);
    }

    private void OnActivate(Entity<IntelligenceOverrideComponent> ent, ref IntelligenceOverrideActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || ent.Comp.NextUseAt > _timing.CurTime ||
            _special.GetEffective(ent, SpecialStat.Intelligence) < 10 || !_mob.IsAlive(ent) ||
            !_blocker.CanInteract(ent, args.Target) || TerminatingOrDeleted(args.Target) ||
            !_interaction.InRangeUnobstructed(ent.Owner, args.Target, range: 2f, popup: true))
            return;

        var target = args.Target;
        bool success;
        if (HasComp<DoorComponent>(target) &&
            (HasComp<AirlockComponent>(target) || HasComp<ApcPowerReceiverComponent>(target)))
        {
            // Hardware capability matters; an outage does not prevent electronic lock bypass.
            success = !_tags.HasTag(target, "EmagImmune") && _emag.DoEmagEffect(ent, target, ignoreDoorPower: true);
        }
        else
        {
            success = TryOverrideRobot(target);
        }
        if (!success)
        {
            _popup.PopupEntity(Loc.GetString("intelligence-override-invalid"), ent, ent);
            return;
        }

        ent.Comp.Selected = false;
        ent.Comp.NextUseAt = _timing.CurTime + Cooldown;
        _actions.SetCooldown(ent.Comp.Action, _timing.CurTime, ent.Comp.NextUseAt);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("intelligence-override-success"), ent, ent);
        _admin.Add(LogType.Emag, LogImpact.High, $"{ToPrettyString(ent):player} used Intelligence Override on {ToPrettyString(target):target}");
        args.Handled = true;
    }

    private void OnVisualSelection(OverrideVisualSelectionEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid ||
            !TryComp<IntelligenceOverrideComponent>(uid, out var ability))
            return;
        ability.Selected = msg.Selected && ability.Action != null && ability.NextUseAt <= _timing.CurTime &&
            _special.GetEffective(uid, SpecialStat.Intelligence) >= 10 && _mob.IsAlive(uid);
        Dirty(uid, ability);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<IntelligenceOverrideComponent>();
        while (query.MoveNext(out var uid, out var ability))
        {
            if (!ability.Selected || HasComp<ActorComponent>(uid) && _mob.IsAlive(uid) &&
                ability.NextUseAt <= _timing.CurTime && _special.GetEffective(uid, SpecialStat.Intelligence) >= 10)
                continue;
            ability.Selected = false;
            Dirty(uid, ability);
        }
    }

    private bool TryOverrideRobot(EntityUid target)
    {
        if (!_factions.IsOverrideRobot(target) || _factions.IsOverrideProtectedPlayer(target) ||
            !TryComp<HTNComponent>(target, out var ai) || !_mob.IsAlive(target) ||
            HasComp<OverriddenRobotComponent>(target))
            return false;

        // Stop the current combat plan before changing allegiance, so cached targets cannot keep firing.
        ai.PlanningToken?.Cancel();
        ai.PlanningToken = null;
        ai.PlanningJob = null;
        if (ai.Plan != null)
        {
            _htn.ShutdownTask(ai.Plan.CurrentOperator, ai.Blackboard, HTNOperatorStatus.Failed);
            _htn.ShutdownPlan(ai);
        }
        RemComp<NPCMeleeCombatComponent>(target);
        RemComp<NPCRangedCombatComponent>(target);
        _steering.Unregister(target, force: true);
        ai.Blackboard.Remove<EntityUid>("Target");
        ai.Blackboard.Remove<EntityUid>(NPCBlackboard.UtilityTarget);
        ai.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
        ai.Blackboard.Remove<Robust.Shared.Map.EntityCoordinates>(NPCBlackboard.FollowTarget);
        ai.Blackboard.Remove<Robust.Shared.Map.EntityCoordinates>(NPCBlackboard.MovementTarget);
        RemComp<RecruitedFollowerComponent>(target);
        RemComp<FollowerAutoRecruitComponent>(target);
        RemComp<Content.Server._Misfits.Silicon.StationAiCommandedNpcComponent>(target);
        // Give former pets/followers the normal autonomous robot combat routine, never an owner.
        ai.RootTask = new HTNCompoundTask { Task = "N14HostileCompound" };
        ai.NextPlanTime = _timing.CurTime;
        _retaliation.ClearRetaliation(target);
        _factions.SetOverrideFaction(target);
        return true;
    }
}
