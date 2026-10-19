using System.Numerics;
using Content.Server._Misfits.Expeditions;
using Content.Shared._Misfits.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Procedural;

[TestFixture]
public sealed class ExpeditionGatherRangeTests
{
    [Test]
    public async Task GatheringUsesExactDistanceAndCurrentPosition()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var system = entities.System<N14ExpeditionSystem>();
            var transforms = entities.System<SharedTransformSystem>();
            var board = entities.SpawnEntity(null, map.GridCoords);
            var target = entities.SpawnEntity(null, new EntityCoordinates(map.GridCoords.EntityId, map.GridCoords.Position + new Vector2(2, 0)));
            Assert.That(system.IsWithinGatherRange(board, target, 2), Is.True);

            transforms.SetCoordinates(target, new EntityCoordinates(map.GridCoords.EntityId, map.GridCoords.Position + new Vector2(2.01f, 0)));
            Assert.That(system.IsWithinGatherRange(board, target, 2), Is.False, "Walking away must remove launch eligibility");
            transforms.SetCoordinates(target, new EntityCoordinates(map.GridCoords.EntityId, map.GridCoords.Position + new Vector2(1.5f, 1.5f)));
            Assert.That(system.IsWithinGatherRange(board, target, 2), Is.False, "Diagonal broadphase candidates outside the circle must be excluded");
            transforms.SetCoordinates(target, otherMap.GridCoords);
            Assert.That(system.IsWithinGatherRange(board, target, 2), Is.False, "Players on another map must never be gathered");
            entities.DeleteEntity(target);
            Assert.That(system.IsWithinGatherRange(board, target, 2), Is.False);
            entities.DeleteEntity(board);

            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var id in new[] { "N14ExpeditionEntranceBunker", "N14ExpeditionEntranceManhole" })
            {
                var component = (N14ExpeditionBoardComponent) prototypes.Index<EntityPrototype>(id).Components["N14ExpeditionBoard"].Component;
                Assert.That(component.GatherRadius, Is.EqualTo(2f));
            }
            Assert.That(new N14ExpeditionBoardComponent().GatherRadius, Is.EqualTo(2f));
        });
        await pair.CleanReturnAsync();
    }
}
