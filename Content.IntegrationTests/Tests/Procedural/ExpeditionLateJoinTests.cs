using System;
using Content.Server.Maps;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Expeditions;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Content.Shared.Roles;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Procedural;

[TestFixture]
public sealed class ExpeditionLateJoinTests
{
    [TestCase(SpawnPointType.LateJoin)]
    [TestCase(SpawnPointType.Job)]
    public async Task LateJoinFallbackStaysOnSurfaceWithAnActiveExpedition(SpawnPointType spawnType)
    {
        await using var pair = await PoolManager.GetServerClient();
        var surface = await pair.CreateTestMap();
        var underground = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(prototypes.Index<JobPrototype>("Wastelander").AlwaysUseSpawner, Is.True);
            var stationSystem = em.System<StationSystem>();
            var station = stationSystem.InitializeNewStation(prototypes.Index<GameMapPrototype>("CommonWealth").Stations["CommonWealth"],
                new[] { surface.Grid.Owner }, "Surface test station");
            // Reproduce the dangerous ordering: a marker on another map is encountered first.
            em.SpawnEntity("N14SpawnPointWastelander", underground.GridCoords);
            var member = em.SpawnEntity("MobHuman", underground.GridCoords);
            var expedition = em.EnsureComponent<N14ExpeditionComponent>(underground.MapUid);
            expedition.GridUid = underground.Grid.Owner;
            var session = new N14ExpeditionSession { EndTime = TimeSpan.MaxValue, ReturnPoint = surface.GridCoords, EntryPoint = underground.GridCoords };
            session.Players.Add(member);
            expedition.Sessions.Add(session);
            em.SpawnEntity("N14SpawnPointWastelander", surface.GridCoords);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var spawned = em.System<StationSpawningSystem>().SpawnPlayerCharacterOnStation(station,
                    new JobComponent { Prototype = "Wastelander" }, HumanoidCharacterProfile.DefaultWithSpecies(),
                    spawnPointType: spawnType);
                Assert.That(spawned, Is.Not.Null, "Late joining and respawning must create a character.");
                Assert.That(em.GetComponent<TransformComponent>(spawned!.Value).MapID, Is.EqualTo(surface.MapId),
                    "A surface job must never use an unrelated expedition marker as its fallback.");
                Assert.That(em.System<MobStateSystem>().IsAlive(spawned.Value), Is.True);
                em.DeleteEntity(spawned.Value);
            }
            em.DeleteEntity(station);
        });
        await pair.CleanReturnAsync();
    }
}
