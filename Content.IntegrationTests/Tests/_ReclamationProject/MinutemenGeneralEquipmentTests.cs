using System.Linq;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class MinutemenGeneralEquipmentTests
{
    [Test]
    public async Task GeneralActuallySpawnsWithBothLoadedWeaponsAndAllSpareAmmo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var job = prototypes.Index<JobPrototype>("MinutemenGeneral");
            var human = em.SpawnEntity("MobHuman", map.GridCoords);
            em.System<StationSpawningSystem>().EquipStartingGear(human, prototypes.Index(job.StartingGear!.Value));
            foreach (var special in job.Special) special.AfterEquip(human);
            Assert.That(em.HasComponent<PowerArmorProficiencyComponent>(human), Is.True);
            var inventory = em.System<InventorySystem>();
            EntityUid Slot(string slot, string prototype)
            {
                Assert.That(inventory.TryGetSlotEntity(human, slot, out var item), Is.True, slot);
                Assert.That(em.GetComponent<MetaDataComponent>(item!.Value).EntityPrototype!.ID, Is.EqualTo(prototype));
                return item.Value;
            }
            Slot("outerClothing", "ReclamationMinutemenGeneralPowerArmor");
            var gatling = Slot("suitstorage", "N14WeaponLaserGatling");
            var pistol = Slot("belt", "ReclamationMinutemenGeneralPistol");
            Slot("id", "MinutemenIDDogtagsGeneral");
            Assert.That(em.System<SharedHandsSystem>().TryGetActiveItem(human, out var held), Is.True);
            Assert.That(em.GetComponent<MetaDataComponent>(held!.Value).EntityPrototype!.ID, Is.EqualTo("MinutemenOfficerCutlass"));
            var bag = Slot("back", "N14ClothingBackpackMilitary");
            var contents = em.GetComponent<StorageComponent>(bag).Container.ContainedEntities;
            string Id(EntityUid entity) => em.GetComponent<MetaDataComponent>(entity).EntityPrototype!.ID;
            Assert.That(contents.Select(Id), Is.EquivalentTo(new[] { "N14BoxPlasticFilledWastelander", "N14FusionCore", "N14FusionCore", "N14MagazinePistol10mm", "N14MagazinePistol10mm", "MagazineBox10mm" }));
            foreach (var ammo in contents.Where(entity => Id(entity) is "N14MagazinePistol10mm" or "MagazineBox10mm"))
                Assert.That(em.GetComponent<BallisticAmmoProviderComponent>(ammo).AmmoCount, Is.GreaterThan(0));
            var slots = em.System<ItemSlotsSystem>();
            Assert.That(Id(slots.GetItemOrNull(gatling, "gun_magazine")!.Value), Is.EqualTo("N14FusionCore"));
            var magazine = slots.GetItemOrNull(pistol, "gun_magazine")!.Value;
            Assert.That(Id(magazine), Is.EqualTo("N14MagazinePistol10mm"));
            Assert.That(em.GetComponent<BallisticAmmoProviderComponent>(magazine).AmmoCount, Is.GreaterThan(0));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RenamedRangerArmorAndHelmetKeepTheirOriginalProtectionAndAttachedPieces()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var original = em.SpawnEntity("MisfitsClothingOuterPowerArmorRanger", map.GridCoords);
            var renamed = em.SpawnEntity("ReclamationMinutemenGeneralPowerArmor", map.GridCoords);
            Assert.That(em.GetComponent<ArmorComponent>(renamed).Modifiers.Coefficients,
                Is.EquivalentTo(em.GetComponent<ArmorComponent>(original).Modifiers.Coefficients));
            Assert.That(em.GetComponent<PowerArmorIntegrityComponent>(renamed).MaxIntegrity,
                Is.EqualTo(em.GetComponent<PowerArmorIntegrityComponent>(original).MaxIntegrity));
            var parts = em.GetComponent<ToggleableClothingComponent>(renamed).ClothingUids.Keys;
            Assert.That(parts.Select(e => em.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID), Is.EquivalentTo(new[]
            { "ReclamationMinutemenGeneralPowerArmorHelmet", "N14ClothingPAGauntlets", "N14ClothingPABoots", "N14ClothingPowerSystemsBack" }));
            var helmet = parts.Single(e => em.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID == "ReclamationMinutemenGeneralPowerArmorHelmet");
            var sourceHelmet = em.SpawnEntity("MisfitsClothingHeadHelmetPowerArmorRanger", map.GridCoords);
            var pistol = em.SpawnEntity("ReclamationMinutemenGeneralPistol", map.GridCoords);
            foreach (var item in new[] { renamed, helmet, pistol })
                Assert.That(em.GetComponent<MetaDataComponent>(item).EntityDescription.ToLowerInvariant(), Does.Not.Contain("ranger"));
            Assert.That(em.GetComponent<ArmorComponent>(helmet).Modifiers.Coefficients,
                Is.EquivalentTo(em.GetComponent<ArmorComponent>(sourceHelmet).Modifiers.Coefficients));
        });
        await pair.CleanReturnAsync();
    }
}
