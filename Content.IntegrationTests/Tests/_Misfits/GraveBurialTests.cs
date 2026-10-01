// #Misfits Add - Integration test: verifies a dug grave can close over a nearby body and reopen it later.
#nullable enable

using Content.IntegrationTests.Pair;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Burial.Components;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Misfits;

[TestFixture]
public sealed class GraveBurialTests
{
    [Test]
    public async Task WoodenGraveCanReburyNearbyBody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var storageSystem = entityManager.System<EntityStorageSystem>();
        var standingSystem = entityManager.System<StandingStateSystem>();
        var containerSystem = entityManager.System<SharedContainerSystem>();

        EntityUid grave = default;
        EntityUid user = default;
        EntityUid body = default;
        GraveComponent? graveComp = null;

        await server.WaitAssertion(() =>
        {
            grave = entityManager.SpawnEntity("CrateWoodenGrave", map.GridCoords);
            user = entityManager.SpawnEntity("MobHumanDummy", map.GridCoords);
            body = entityManager.SpawnEntity("MobHumanDummy", new EntityCoordinates(grave, 0.3f, 0f));

            Assert.That(standingSystem.Down(body), Is.True);

            Assert.That(entityManager.TryGetComponent(grave, out graveComp), Is.True);
            Assert.That(graveComp, Is.Not.Null);

            Assert.That(storageSystem.TryOpenStorage(user, grave), Is.False, "An undug grave should not open.");

            graveComp!.DiggingComplete = true;
            Assert.That(storageSystem.TryOpenStorage(user, grave), Is.True, "A dug grave should open.");

            graveComp.DiggingComplete = true;
            Assert.That(storageSystem.TryCloseStorage(grave), Is.True, "A dug grave should close again.");
            Assert.That(containerSystem.IsEntityInContainer(body), Is.True, "Closing the grave should bury the nearby body.");

            graveComp.DiggingComplete = true;
            Assert.That(storageSystem.TryOpenStorage(user, grave), Is.True, "A re-dug grave should reopen.");
            Assert.That(containerSystem.IsEntityInContainer(body), Is.False, "Reopening the grave should release the buried body.");
        });

        await pair.CleanReturnAsync();
    }
}