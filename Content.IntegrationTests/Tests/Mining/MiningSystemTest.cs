using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Mining;

public sealed class MiningSystemTest
{
    [Test]
    public async Task DestroyingStackedOreVeinDropsForWholeStack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var timber = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            timber = entityManager.SpawnEntity("N14FloraLogTimber", testMap.GridCoords);
            entityManager.GetComponent<StackComponent>(timber).Count = 10;
        });

        await server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier(prototypeManager.Index<DamageGroupPrototype>("Brute"), 50);
            entityManager.System<DamageableSystem>().TryChangeDamage(timber, damage, true);

            var nearby = entityManager.System<EntityLookupSystem>()
                .GetEntitiesInRange(testMap.GridCoords, 3, LookupFlags.All | LookupFlags.Approximate);
            var plankStacks = nearby
                .Where(entity => entityManager.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == "MaterialWoodPlank1")
                .Select(entity => entityManager.GetComponent<StackComponent>(entity).Count)
                .OrderByDescending(count => count)
                .ToArray();

            Assert.That(plankStacks, Is.EqualTo(new[] { 30, 20 }));
        });

        await pair.CleanReturnAsync();
    }
}
