using System.Linq;
using Content.Server.Atmos.Rotting;
using Content.Server.Storage.Components;
using Content.Server.UndecidedLoadout.Components;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Item;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class WastelandChefKitTests
{
    [Test]
    public async Task KitSuppliesToolsCookbookCondimentsAndAllRecipeMeatTypes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var kit = em.SpawnEntity("ReclamationWastelanderKitChef", map.GridCoords);
            var contents = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(kit).Items);
            Assert.That(contents, Is.EquivalentTo(new[]
            {
                "N14ButchersCleaver", "N14KitchenKnife", "ClothingBeltChef", "ReclamationCookbook",
                "N14FoodShakerSalt", "N14FoodShakerPepper", "ReclamationChefFoodGrabBagFilled"
            }));
            var selector = em.SpawnEntity("ReclamationWastelanderKitSelector", map.GridCoords);
            Assert.That(em.GetComponent<UndecidedLoadoutBackpackComponent>(selector).PossibleSets.Select(id => id.ToString()),
                Does.Contain("ReclamationWastelanderSetChef"));

            var bag = em.SpawnEntity("ReclamationChefFoodGrabBagFilled", map.GridCoords);
            var stored = em.GetComponent<StorageComponent>(bag).Container.ContainedEntities
                .Select(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID).ToArray();
            Assert.That(stored, Has.Length.EqualTo(27), "All ingredients must fit inside the bag.");
            Assert.That(stored.Count(id => id == "ReagentContainerFlour"), Is.EqualTo(2));
            Assert.That(stored, Does.Contain("ReagentContainerRice"));
            Assert.That(stored, Does.Contain("ReagentContainerCornmeal"));
            var meats = stored.Where(id => id.Contains("Meat")).ToArray();
            Assert.That(meats, Has.Length.EqualTo(23));
            Assert.That(meats.Distinct().Count(), Is.EqualTo(23));
            Assert.That(meats, Does.Contain("N14FoodMeatSupermutant"));
            Assert.That(meats, Does.Contain("FoodMeatFish"));
            Assert.That(meats, Does.Not.Contain("N14FoodMeatSquirrel"));
            Assert.That(em.GetComponent<ItemComponent>(bag).Shape, Is.EquivalentTo(new[] { new Box2i(0, 0, 1, 1) }));

            var belt = em.SpawnEntity("ClothingBeltChef", map.GridCoords);
            foreach (var id in new[] { "N14ButchersCleaver", "N14KitchenKnife", "N14FoodShakerSalt", "N14FoodShakerPepper" })
            {
                var item = em.SpawnEntity(id, map.GridCoords);
                Assert.That(em.System<SharedStorageSystem>().CanInsert(belt, item, out _), Is.True, id);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PreservationWorksInsideBackpacksAndStopsWhenFoodIsRemoved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid removedMeat = default;
        await pair.Server.WaitAssertion(() =>
        {
            var bag = em.SpawnEntity("ReclamationChefFoodGrabBagFilled", map.GridCoords);
            var backpack = em.SpawnEntity("ClothingBackpack", map.GridCoords);
            var meat = em.GetComponent<StorageComponent>(bag).Container.ContainedEntities
                .First(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID == "N14FoodMeatBrahmin");
            var rot = em.System<RottingSystem>();
            var perishable = em.GetComponent<PerishableComponent>(meat);
            Assert.That(rot.IsRotProgressing(meat, perishable), Is.False);
            Assert.That(em.System<SharedStorageSystem>().Insert(backpack, bag, out _), Is.True);
            Assert.That(rot.IsRotProgressing(meat, perishable), Is.False, "A backpack must not override the preserving sack.");
            Assert.That(em.System<SharedContainerSystem>().TryRemoveFromContainer(meat, true), Is.True);
            Assert.That(rot.IsRotProgressing(meat, perishable), Is.True);
            removedMeat = meat;
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            var ordinaryBag = em.SpawnEntity("ClothingBackpack", map.GridCoords);
            Assert.That(em.System<SharedStorageSystem>().Insert(ordinaryBag, removedMeat, out _, out var reason), Is.True, reason);
            Assert.That(em.System<RottingSystem>().IsRotProgressing(removedMeat, em.GetComponent<PerishableComponent>(removedMeat)),
                Is.True, "Ordinary backpacks must not preserve food.");
        });
        await pair.CleanReturnAsync();
    }
}
