using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared._NC.Sponsor;
using Content.Shared.CCVar;
using Content.Shared.Customization.Systems;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Server.Console;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - the faction heads (Institute Director, Minutemen General) have a slot on Common Wealth,
/// actually spawn there the way round start spawns them, and a job-whitelisted player can take them without hours.
/// </summary>
[TestFixture]
public sealed class FactionHeadSpawnTest
{
    private static readonly string[] Heads = { "InstituteDirector", "MinutemenGeneral" };

    [Test]
    public async Task FactionHeadsCanSpawnOnCommonWealth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            InLobby = true,
            Map = "CommonWealth",
            Dirty = true
        });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var console = server.ResolveDependency<IServerConsoleHost>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var sponsor = server.ResolveDependency<ISharedSponsorManager>();

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.GameMap, "CommonWealth");
            console.ExecuteCommand("cvar game.map_rotation false");
            console.ExecuteCommand("setgamepreset Sandbox");
            entities.System<GameTicker>().StartRound();
        });
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var station = entities.System<StationSystem>().GetStations().Single();
            // The station entity itself sits in nullspace; its grid is what's on the map.
            var grid = entities.System<StationSystem>().GetLargestGrid(entities.GetComponent<Content.Server.Station.Components.StationDataComponent>(station));
            Assert.That(grid, Is.Not.Null, "Common Wealth station has no grid");
            var stationMap = entities.GetComponent<TransformComponent>(grid!.Value).MapID;
            var jobs = entities.System<StationJobsSystem>();
            var spawning = entities.System<StationSpawningSystem>();
            var oldTimers = config.GetCVar(CCVars.GameRoleTimers);
            config.SetCVar(CCVars.GameRoleTimers, true);
            try
            {
                foreach (var id in Heads)
                {
                    var job = prototypes.Index<JobPrototype>(id);

                    // A slot on the map.
                    Assert.That(jobs.TryGetJobSlot(station, id, out var slots), Is.True, $"{id} has no slot on Common Wealth");
                    Assert.That(slots, Is.GreaterThan(0u), $"{id} has zero slots on Common Wealth");

                    // Spawns the way round start does for alwaysUseSpawner jobs (job spawn point, then the station fallback).
                    Assert.That(job.AlwaysUseSpawner, Is.True, $"{id} no longer uses spawners; update this test");
                    var mob = spawning.SpawnPlayerCharacterOnStation(station, new JobComponent { Prototype = id },
                        HumanoidCharacterProfile.DefaultWithSpecies(), spawnPointType: SpawnPointType.Job);
                    Assert.That(mob, Is.Not.Null, $"{id} did not spawn on Common Wealth");
                    Assert.That(entities.GetComponent<TransformComponent>(mob!.Value).MapID, Is.EqualTo(stationMap),
                        $"{id} spawned off the Common Wealth map");

                    // No hours at all: locked without the job whitelist, open with it.
                    var times = prototypes.EnumeratePrototypes<PlayTimeTrackerPrototype>().ToDictionary(t => t.ID, _ => TimeSpan.Zero);
                    bool Eligible(bool whitelisted) => entities.System<CharacterRequirementsSystem>().CheckRequirementsValid(
                        job.Requirements!, job, HumanoidCharacterProfile.DefaultWithSpecies(), times, true, job,
                        entities, prototypes, config, sponsor, out _, jobWhitelisted: whitelisted);
                    Assert.That(Eligible(false), Is.False, $"{id} opened with no hours and no whitelist");
                    Assert.That(Eligible(true), Is.True, $"{id} stayed locked for a whitelisted player with no hours");
                }
            }
            finally
            {
                config.SetCVar(CCVars.GameRoleTimers, oldTimers);
            }
        });

        await pair.CleanReturnAsync();
    }
}
