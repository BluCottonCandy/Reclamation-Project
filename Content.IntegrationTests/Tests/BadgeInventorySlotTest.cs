using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class BadgeInventorySlotTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: BadgeSlotHumanDummy
  components:
  - type: Inventory
  - type: ContainerContainer
  - type: IdCard

- type: entity
  parent: BadgeSlotHumanDummy
  id: BadgeSlotSupermutantDummy
  components:
  - type: Inventory
    templateId: supermutant

- type: entity
  parent: BadgeSlotHumanDummy
  id: BadgeSlotC27Dummy
  components:
  - type: Inventory
    templateId: c27

- type: entity
  id: BadgeSlotCoatDummy
  components:
  - type: Item
  - type: Clothing
    slots: [NECK]
  - type: Tag
    tags: [SuperMutantWearable]
";

    [TestCase("BadgeSlotHumanDummy")]
    [TestCase("BadgeSlotSupermutantDummy")]
    [TestCase("BadgeSlotC27Dummy")]
    public async Task BadgeAndNeckItemsCanBeWornIndependently(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<InventorySystem>();
            var wearer = entities.SpawnEntity(prototype, map.GridCoords);
            var coat = entities.SpawnEntity("BadgeSlotCoatDummy", map.GridCoords);
            var pin = entities.SpawnEntity("ClothingNeckPinMinutemenSergeant", map.GridCoords);
            var id = entities.GetComponent<IdCardComponent>(wearer);

            Assert.That(inventory.CanEquip(wearer, coat, "badge", out _), Is.False,
                "The badge slot must reject coats even though both slots accept neck clothing.");
            Assert.That(inventory.TryEquip(wearer, coat, "neck"), Is.True);
            Assert.That(inventory.TryEquip(wearer, pin, "badge"), Is.True);
            Assert.That(id.LocalizedJobTitle, Is.EqualTo("Sergeant"));

            Assert.That(inventory.TryUnequip(wearer, "neck"), Is.True);
            Assert.That(inventory.TryGetSlotEntity(wearer, "badge", out var wornPin), Is.True);
            Assert.That(wornPin, Is.EqualTo(pin));
            Assert.That(id.LocalizedJobTitle, Is.EqualTo("Sergeant"));

            Assert.That(inventory.TryEquip(wearer, coat, "neck"), Is.True);
            Assert.That(inventory.TryUnequip(wearer, "badge"), Is.True);
            Assert.That(inventory.TryGetSlotEntity(wearer, "neck", out var wornCoat), Is.True);
            Assert.That(wornCoat, Is.EqualTo(coat));
            Assert.That(id.LocalizedJobTitle, Is.Null);

            // Existing neck-only pins must still apply their rank title.
            Assert.That(inventory.TryUnequip(wearer, "neck"), Is.True);
            Assert.That(inventory.TryEquip(wearer, pin, "neck"), Is.True);
            Assert.That(id.LocalizedJobTitle, Is.EqualTo("Sergeant"));

            entities.DeleteEntity(wearer);
            entities.DeleteEntity(coat);
        });

        await pair.CleanReturnAsync();
    }
}
