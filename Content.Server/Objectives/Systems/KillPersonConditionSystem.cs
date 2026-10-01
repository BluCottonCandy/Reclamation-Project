using System.Linq;
using Content.Server.Objectives.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.NPC.Components; // #Misfits Change /Add/
using Content.Shared.NPC.Systems; // #Misfits Change /Add/
using Content.Shared.Objectives.Components;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server.Objectives.Systems;

/// <summary>
/// Handles kill person condition logic and picking random kill targets.
/// </summary>
public sealed class KillPersonConditionSystem : EntitySystem
{
    [Dependency] private readonly EmergencyShuttleSystem _emergencyShuttle = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!; // #Misfits Change /Add/
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedJobSystem _job = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly TargetObjectiveSystem _target = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KillPersonConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);

        SubscribeLocalEvent<PickRandomPersonComponent, ObjectiveAssignedEvent>(OnPersonAssigned);

        SubscribeLocalEvent<PickRandomHeadComponent, ObjectiveAssignedEvent>(OnHeadAssigned);

        // #Misfits Change /Add/ - faction-aware head kill target
        SubscribeLocalEvent<PickRandomEnemyHeadComponent, ObjectiveAssignedEvent>(OnEnemyHeadAssigned);
    }

    private void OnGetProgress(EntityUid uid, KillPersonConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_target.GetTarget(uid, out var target))
            return;

        args.Progress = GetProgress(target.Value, comp.RequireDead);
    }

    private void OnPersonAssigned(EntityUid uid, PickRandomPersonComponent comp, ref ObjectiveAssignedEvent args)
    {
        // invalid objective prototype
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        // target already assigned
        if (target.Target != null)
            return;

        // no other humans to kill
        var allHumans = _mind.GetAliveHumans(args.MindId, comp.NeedsOrganic);
        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        _target.SetTarget(uid, _random.Pick(allHumans), target);
    }

    private void OnHeadAssigned(EntityUid uid, PickRandomHeadComponent comp, ref ObjectiveAssignedEvent args)
    {
        // invalid prototype
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        // target already assigned
        if (target.Target != null)
            return;

        // no other humans to kill
        var allHumans = _mind.GetAliveHumans(args.MindId);
        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        var allHeads = new List<EntityUid>();
        foreach (var mind in allHumans)
        {
            // RequireAdminNotify used as a cheap way to check for command department
            if (_job.MindTryGetJob(mind, out _, out var prototype) && prototype.RequireAdminNotify)
                allHeads.Add(mind);
        }

        if (allHeads.Count == 0)
            allHeads = allHumans.Select(human => human.Owner).ToList(); // fallback to non-head target

        _target.SetTarget(uid, _random.Pick(allHeads), target);
    }

    // #Misfits Change /Add/ - kill target that excludes the assigner's own faction
    private void OnEnemyHeadAssigned(EntityUid uid, PickRandomEnemyHeadComponent comp, ref ObjectiveAssignedEvent args)
    {
        // invalid prototype
        if (!TryComp<TargetObjectiveComponent>(uid, out var target))
        {
            args.Cancelled = true;
            return;
        }

        // target already assigned
        if (target.Target != null)
            return;

        // resolve assigner's body to read their faction membership
        if (!TryComp<MindComponent>(args.MindId, out var assignerMind) || assignerMind.OwnedEntity == null)
        {
            args.Cancelled = true;
            return;
        }

        TryComp<NpcFactionMemberComponent>(assignerMind.OwnedEntity.Value, out var assignerFactionComp);

        // get every alive human except ourselves
        var allHumans = _mind.GetAliveHumans(args.MindId);
        if (allHumans.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        // filter out same-faction members — they should never be targeted
        var enemies = new List<EntityUid>();
        foreach (var mindEntity in allHumans)
        {
            var candidateMind = mindEntity.Comp;
            if (candidateMind.OwnedEntity == null)
                continue;

            // if the player has a known faction and the candidate shares it, skip them
            // #Misfits Change /Fix/ - use NpcFactionSystem.IsMemberOfAny instead of .Factions.Overlaps() (RA0002)
            if (assignerFactionComp != null
                && _npcFaction.IsMemberOfAny(candidateMind.OwnedEntity.Value, assignerFactionComp.Factions))
                continue;

            enemies.Add(mindEntity.Owner);
        }

        if (enemies.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        // prefer command heads (RequireAdminNotify job), fall back to any non-same-faction person
        var enemyHeads = new List<EntityUid>();
        foreach (var enemyMind in enemies)
        {
            if (_job.MindTryGetJob(enemyMind, out _, out var prototype) && prototype.RequireAdminNotify)
                enemyHeads.Add(enemyMind);
        }

        if (enemyHeads.Count == 0)
            enemyHeads = enemies;

        _target.SetTarget(uid, _random.Pick(enemyHeads), target);
    }

    private float GetProgress(EntityUid target, bool requireDead)
    {
        // deleted or gibbed or something, counts as dead
        if (!TryComp<MindComponent>(target, out var mind) || mind.OwnedEntity == null)
            return 1f;

        // dead is success
        if (_mind.IsCharacterDeadIc(mind))
            return 1f;

        // if the target has to be dead dead then don't check evac stuff
        if (requireDead)
            return 0f;

        // if evac is disabled then they really do have to be dead
        if (!_config.GetCVar(CCVars.EmergencyShuttleEnabled))
            return 0f;

        // target is escaping so you fail
        if (_emergencyShuttle.IsTargetEscaping(mind.OwnedEntity.Value))
            return 0f;

        // evac has left without the target, greentext since the target is afk in space with a full oxygen tank and coordinates off.
        if (_emergencyShuttle.ShuttlesLeft)
            return 1f;

        // if evac is still here and target hasn't boarded, show 50% to give you an indicator that you are doing good
        return _emergencyShuttle.EmergencyShuttleArrived ? 0.5f : 0f;
    }
}
