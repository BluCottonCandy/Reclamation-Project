using System.Linq;
using Content.Shared._ReclamationProject.Blindness;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class BlindfoldStartingBagTests
{
    [TestCase("N14ClothingBackpackTrekker", false)]
    [TestCase("N14ClothingBackpackDuffel", false)]
    [TestCase("N14ClothingBackpackSatchelTrekker", false)]
    [TestCase("N14ClothingBackpackTrekker", true)]
    [TestCase("N14ClothingBackpackDuffel", true)]
    [TestCase("N14ClothingBackpackSatchelTrekker", true)]
    public async Task StartingBlindfoldUsesFinalBackSlotBag(string bagId, bool replaceAfterTrait)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid wearer = default, bag = default, oldBag = default;
        await pair.Server.WaitAssertion(() =>
        {
            wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
            var inventory = entities.System<InventorySystem>();
            if (replaceAfterTrait)
            {
                oldBag = entities.SpawnEntity("ClothingBackpack", map.GridCoords);
                Assert.That(inventory.TryEquip(wearer, oldBag, "back", force: true), Is.True);
                entities.AddComponent<BlindConcentrationComponent>(wearer);
                // Match Exclusive loadouts: the old bag and its contents are deleted.
                entities.DeleteEntity(oldBag);
            }
            bag = entities.SpawnEntity(bagId, map.GridCoords);
            Assert.That(inventory.TryEquip(wearer, bag, "back", force: true), Is.True);
            if (!replaceAfterTrait)
                entities.AddComponent<BlindConcentrationComponent>(wearer);
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
        {
            var contents = entities.GetComponent<StorageComponent>(bag).Container.ContainedEntities;
            Assert.That(contents.Count(item => entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "ClothingEyesBlindfold"), Is.EqualTo(1));
            if (replaceAfterTrait)
            {
                Assert.That(entities.EntityExists(oldBag), Is.False);
            }
            entities.DeleteEntity(wearer);
        });
        await pair.CleanReturnAsync();
    }
}
