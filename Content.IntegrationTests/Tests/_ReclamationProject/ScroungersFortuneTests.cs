using System;
using System.Linq;
using Content.Server._ReclamationProject.Luck;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class ScroungersFortuneTests
{
    [Test]
    public async Task RaritiesAndNestedLootStayWithinTheirLimits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            string[] piles = ["ReclamationOldScrap", "ReclamationOverlookedScrap", "ReclamationEasyToMissPile", "ReclamationSomethingShiny"];
            int[] weights = [50, 35, 10, 5];
            int[] minimum = [3, 2, 2, 1];
            int[] maximum = [3, 2, 2, 3];
            string[] tables = ["ReclamationFortuneOldLoot", "ReclamationFortuneOverlookedLoot", "ReclamationFortuneHiddenLoot", "ReclamationFortuneShinyLoot"];
            for (var i = 0; i < piles.Length; i++)
            {
                Assert.That(Enumerable.Range(0, 100).Count(roll => ScroungersFortuneSystem.SelectPile(roll) == piles[i]), Is.EqualTo(weights[i]));
                Assert.That(prototypes.HasIndex<EntityPrototype>(piles[i]), Is.True);
                var table = new NestedSelector { TableId = tables[i] };
                var sawThree = false;
                var random = new Random(314159);
                for (var roll = 0; roll < 1000; roll++)
                {
                    var loot = em.System<EntityTableSystem>().GetSpawns(table, random).ToArray();
                    Assert.That(loot.Length, Is.InRange(minimum[i], maximum[i]), tables[i]);
                    sawThree |= loot.Length == 3;
                    foreach (var item in loot)
                        Assert.That(prototypes.HasIndex<EntityPrototype>(item), Is.True, item.ToString());
                }
                if (maximum[i] == 3)
                    Assert.That(sawThree, Is.True, "Three-item finds must be possible.");
            }
            Assert.That(ScroungersFortuneSystem.Cooldown, Is.EqualTo(TimeSpan.FromMinutes(10)));
        });
        await pair.CleanReturnAsync();
    }
}
