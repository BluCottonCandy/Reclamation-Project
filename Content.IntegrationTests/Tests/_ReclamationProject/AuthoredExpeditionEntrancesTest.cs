using System.Linq;
using System;
using System.Numerics;
using Content.Server._Misfits.WastelandMap;
using Content.Shared._Misfits.WastelandMap;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Content.Server._ReclamationProject.Expeditions;
using Content.Shared._Misfits.Expeditions;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AuthoredExpeditionEntrancesTest
{
    [Test]
    public async Task LegacyEntrancesUseAuthoredPools()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var prototype in new[] { "N14ExpeditionEntranceSquareLadder", "N14ExpeditionEntranceManhole", "N14ExpeditionEntranceBunker", "N14ExpeditionEntranceBoardedWell", "FactoryDeparture" })
            {
                var entrance = entities.SpawnEntity(prototype, map.GridCoords);
                Assert.That(entities.HasComponent<N14ExpeditionBoardComponent>(entrance), Is.False, prototype);
                var departure = entities.GetComponent<FactoryDepartureComponent>(entrance);
                Assert.That(departure.MapPool.Select(path => path.ToString()), Is.EquivalentTo(new[]
                {
                    "/Maps/Reclamation/Expeditions/Factory.yml", "/Maps/Reclamation/Expeditions/Cave1.yml",
                }), prototype);
                Assert.That(departure.MediumMapPool.Select(path => path.ToString()), Is.EquivalentTo(new[] { "/Maps/Reclamation/Expeditions/Factory2.yml" }), prototype);
                Assert.That(departure.HardMapPool.Select(path => path.ToString()), Is.EquivalentTo(new[] { "/Maps/Reclamation/Expeditions/Factory3.yml" }), prototype);
                Assert.That(departure.Duration, Is.EqualTo(1200), prototype);
                entities.QueueDeleteEntity(entrance);
            }
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task AuthoredEntrancesAppearOnceOnEachMapFeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var otherMap = entities.System<SharedMapSystem>().CreateMap(out _, runMapInit: true);
            var mapSystem = entities.System<WastelandMapSystem>();
            var view = new WastelandMapComponent
            {
                MapTexturePath = new ResPath("_Misfits/Maps/wendover_map.png"),
                WorldBounds = new Box2(-10, -10, 10, 10),
            };
            var entrances = new System.Collections.Generic.List<EntityUid>();
            try
            {
                var prototypes = new[] { "N14ExpeditionEntranceSquareLadder", "N14ExpeditionEntranceManhole", "N14ExpeditionEntranceBunker", "N14ExpeditionEntranceBoardedWell", "FactoryDeparture" };
                for (var i = 0; i < prototypes.Length; i++)
                    entrances.Add(entities.SpawnEntity(prototypes[i], new EntityCoordinates(map.Grid.Owner, new Vector2(i, 0))));
                entities.SpawnEntity("FactoryDeparture", new EntityCoordinates(otherMap, Vector2.Zero));
                entities.SpawnEntity("FactoryDeparture", new EntityCoordinates(map.MapUid, new Vector2(100, 100)));
                // A marker without a functioning entrance must not create a phantom destination.
                var landmark = entities.SpawnEntity(null, map.GridCoords);
                entities.EnsureComponent<N14ExpeditionEntranceLandmarkComponent>(landmark);
                foreach (var feed in Enum.GetValues<WastelandMapTacticalFeedKind>())
                {
                    var blips = mapSystem.BuildState(view, map.MapId, feed).TrackedBlips
                        .Where(blip => blip.Kind == WastelandMapTrackedBlipKind.ExpeditionEntrance).ToArray();
                    Assert.That(blips.Length, Is.EqualTo(prototypes.Length), feed.ToString());
                    Assert.That(blips.Select(blip => blip.X), Is.EquivalentTo(new[] { 0f, 1f, 2f, 3f, 4f }), feed.ToString());
                    Assert.That(blips.All(blip => blip.Y == 0 && !string.IsNullOrWhiteSpace(blip.Label)), Is.True);
                }
                entities.DeleteEntity(entrances[0]);
                Assert.That(mapSystem.BuildState(view, map.MapId).TrackedBlips.Count(blip =>
                    blip.Kind == WastelandMapTrackedBlipKind.ExpeditionEntrance), Is.EqualTo(4),
                    "Deleting an entrance removes its map marker");
            }
            finally
            {
                entities.DeleteEntity(otherMap);
            }
        });
        await pair.CleanReturnAsync();
    }
}
