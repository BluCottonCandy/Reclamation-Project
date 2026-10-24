using Content.Shared._ReclamationProject.Institute.Courser;
using Content.Server.GameTicking;
using Content.Shared.NPC.Systems;

namespace Content.Server._ReclamationProject.Institute.Courser;

/// <summary>
/// Job specials can replace a spawning character's NPC factions (e.g. a Minutemen job sets only
/// Minutemen). Coursers get the Institute faction added back once spawning is done.
/// </summary>
public sealed class CourserSynthSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _faction = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        EnsureInstitute(args.Mob);
    }

    /// <summary>
    /// Adds the Institute faction to a Courser if a job took it away.
    /// </summary>
    public void EnsureInstitute(EntityUid uid)
    {
        if (TryComp<CourserSynthComponent>(uid, out var courser) && !_faction.IsMember(uid, courser.Faction))
            _faction.AddFaction(uid, courser.Faction);
    }
}
