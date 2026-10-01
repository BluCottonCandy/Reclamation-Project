// #Misfits Change - Route players to faction-specific objective pools (NCRObjectives / BOSWObjectives)
// to eliminate mismatched-requirement warnings caused by the shared N14Objectives pool.
// #Misfits Add - Non-faction players (wastelanders, civilians, etc.) receive the SurviveObjective instead.
// #Misfits Tweak - Faction kill/steal objectives removed (low-RP, encourages griefing).
//                  All players now receive only the universal SurviveObjective.
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Shared.Mind;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking.Rules;

public sealed class N14RuleSystem : GameRuleSystem<N14RuleComponent>
{
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly ObjectivesSystem _objectives = default!;

    // #Misfits Tweak - Faction IDs kept for reference but routing is disabled;
    //                  all players receive SurviveObjective regardless of faction.
    // private static readonly ProtoId<NpcFactionPrototype> NCRFaction = "NCR";
    // private static readonly ProtoId<NpcFactionPrototype> BOSWFaction = "BrotherhoodOfSteel";
    // private static readonly ProtoId<NpcFactionPrototype> LegionFaction = "CaesarLegion";

    // Survive objective prototype ID — the only objective automatically assigned now
    private const string SurviveObjectiveId = "SurviveObjective";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var query = EntityQueryEnumerator<N14RuleComponent>();
        while (query.MoveNext(out var uid, out var rule))
        {
            if (_mindSystem.TryGetMind(args.Player, out var mindId, out var mind))
            {
                // Don't add more objectives if the player already has some (e.g. respawning)
                if (mind.Objectives.Count > 0)
                    return;

                // #Misfits Tweak - Give every player the universal SurviveObjective.
                // Faction-specific kill/steal objectives were disabled: they encouraged
                // griefing and did not fit the wasteland roleplay focus of this server.
                var surviveObjective = _objectives.TryCreateObjective(mindId, mind, SurviveObjectiveId);
                if (surviveObjective != null)
                {
                    Logger.DebugS("n14rule", $"Added SurviveObjective for {args.Player}");
                    _mindSystem.AddObjective(mindId, mind, surviveObjective.Value);
                }
                else
                {
                    Logger.DebugS("n14rule", $"Could not create SurviveObjective for {args.Player}");
                }

                // #Misfits Tweak - Old faction-routing code (disabled):
                // var objectiveGroup = GetObjectiveGroupForFaction(mindId, mind);
                // if (objectiveGroup == null)
                // {
                //     var survive = _objectives.TryCreateObjective(mindId, mind, SurviveObjectiveId);
                //     if (survive != null) _mindSystem.AddObjective(mindId, mind, survive.Value);
                // }
                // else
                // {
                //     var obj = _objectives.GetRandomObjective(mindId, mind, objectiveGroup);
                //     if (obj != null) _mindSystem.AddObjective(mindId, mind, obj.Value);
                // }
            }
            else
            {
                Logger.DebugS("n14rule", $"{args.Player} has no mind");
            }

            // break out of loop: we only need to do this once
            break;
        }
    }

    // #Misfits Tweak - GetObjectiveGroupForFaction disabled; faction kill/steal pools are no longer used.
    // private string? GetObjectiveGroupForFaction(EntityUid mindId, MindComponent mind)
    // {
    //     if (_mindSystem.InFaction(mindId, mind, new HashSet<ProtoId<NpcFactionPrototype>> { NCRFaction }))
    //         return "NCRObjectives";
    //     if (_mindSystem.InFaction(mindId, mind, new HashSet<ProtoId<NpcFactionPrototype>> { BOSWFaction }))
    //         return "BOSWObjectives";
    //     if (_mindSystem.InFaction(mindId, mind, new HashSet<ProtoId<NpcFactionPrototype>> { LegionFaction }))
    //         return "LegionObjectives";
    //     return null;
    // }
}
