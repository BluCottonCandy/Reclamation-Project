using System.Linq;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Kitchen;

public sealed class SharpSystemTest
{
    [Test]
    public async Task ButcheringStackedItemDropsForWholeStack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var user = EntityUid.Invalid;
        var knife = EntityUid.Invalid;
        var gauze = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            user = entityManager.SpawnEntity("MobHuman", testMap.GridCoords);
            knife = entityManager.SpawnEntity("KitchenKnife", testMap.GridCoords);
            gauze = entityManager.SpawnEntity("N14DirtyGauze10", testMap.GridCoords);
            entityManager.System<TagSystem>().AddTag(user, "InstantDoAfters");

            var ev = new AfterInteractEvent(user, knife, gauze, testMap.GridCoords, true);
            entityManager.EventBus.RaiseLocalEvent(knife, ev);
        });

        await server.WaitAssertion(() =>
        {
            var nearby = entityManager.System<EntityLookupSystem>()
                .GetEntitiesInRange(testMap.GridCoords, 3, LookupFlags.All | LookupFlags.Approximate);
            var clothStacks = nearby
                .Where(entity => entityManager.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == "MaterialCloth1")
                .Select(entity => entityManager.GetComponent<StackComponent>(entity).Count)
                .OrderByDescending(count => count)
                .ToArray();

            Assert.That(clothStacks, Is.EqualTo(new[] { 20 }));
        });

        await pair.CleanReturnAsync();
    }
}
