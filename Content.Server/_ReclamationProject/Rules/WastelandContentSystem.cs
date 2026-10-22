using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.StationEvents.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Respawn;

namespace Content.Server._ReclamationProject.Rules;

/// <summary>Excludes station events and legacy nuclear equipment from the wasteland preset.</summary>
public sealed partial class WastelandContentSystem : EntitySystem
{
    [Dependency] private GameTicker _ticker = default!;

    public bool IsRestrictedRound => _ticker.IsGameRuleAdded<N14RuleComponent>();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GameRuleAddedEvent>(OnRuleAdded);
        SubscribeLocalEvent<RemovedWastelandContentComponent, MapInitEvent>(OnMapInit);
    }

    public bool IsBlockedRule(EntityUid uid)
    {
        return IsRestrictedRound && (HasComp<StationEventComponent>(uid) ||
            HasComp<BasicStationEventSchedulerComponent>(uid) ||
            HasComp<RampingStationEventSchedulerComponent>(uid) ||
            HasComp<OscillatingStationEventSchedulerComponent>(uid));
    }

    private void OnRuleAdded(ref GameRuleAddedEvent args)
    {
        if (!HasComp<N14RuleComponent>(args.RuleEntity))
            return;

        foreach (var entity in Snapshot<RemovedWastelandContentComponent>())
            RemoveEquipment(entity);

        // Cancel rules queued before the preset was selected as well as active schedulers.
        foreach (var entity in Snapshot<GameRuleComponent>())
        {
            if (!IsBlockedRule(entity))
                continue;

            RemComp<DelayedStartRuleComponent>(entity);
            _ticker.EndGameRule(entity);
        }
    }

    private void OnMapInit(EntityUid uid, RemovedWastelandContentComponent comp, MapInitEvent args)
    {
        if (IsRestrictedRound)
            RemoveEquipment(uid);
    }

    private List<EntityUid> Snapshot<T>() where T : IComponent
    {
        var result = new List<EntityUid>();
        var query = AllEntityQuery<T>();
        while (query.MoveNext(out var uid, out _))
            result.Add(uid);
        return result;
    }

    private void RemoveEquipment(EntityUid uid)
    {
        // Authentication disks normally replace themselves when deleted.
        if (TryComp<SpecialRespawnComponent>(uid, out var respawn))
            respawn.Respawn = false;

        QueueDel(uid);
    }
}
