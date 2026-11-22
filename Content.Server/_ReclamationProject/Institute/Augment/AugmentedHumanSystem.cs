using Content.Server._ReclamationProject.Medical.BioMonitor;
using Content.Server.GameTicking;
using Content.Shared._ReclamationProject.Institute.Augment;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Institute.Augment;

/// <summary>
/// #Reclamation Project - the Augmented Human (the Director's race):
/// - job specials can replace a spawning character's NPC factions, so the Institute faction is added back once
///   spawning is done (the same as Coursers);
/// - their nanites push stuck shrapnel and bullet fragments out, one every few seconds. A stuck fragment keeps
///   dealing small damage, which would otherwise reset the nanites' wait forever and stop them regenerating.
/// </summary>
public sealed class AugmentedHumanSystem : EntitySystem
{
    [Dependency] private readonly NpcFactionSystem _faction = default!;
    [Dependency] private readonly BioMonitorSystem _bioMonitor = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>How often the nanites push one fragment out.</summary>
    private static readonly TimeSpan PushInterval = TimeSpan.FromSeconds(8);
    private TimeSpan _nextPush;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (TryComp<AugmentedHumanComponent>(args.Mob, out var augmented) && !_faction.IsMember(args.Mob, augmented.Faction))
            _faction.AddFaction(args.Mob, augmented.Faction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextPush)
            return;
        _nextPush = _timing.CurTime + PushInterval;

        var query = EntityQueryEnumerator<AugmentedHumanComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_mobState.IsDead(uid) || !_bioMonitor.HasStuckFragments(uid))
                continue;
            if (_bioMonitor.PushOutOneFragment(uid))
                _popup.PopupEntity(Loc.GetString("augmented-nanites-push-fragment"), uid, uid);
        }
    }
}
