using System.Linq;
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
}
