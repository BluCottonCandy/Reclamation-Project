using System.Linq;
using Content.Server.Maps;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Radio.Components;
using Content.Shared.Storage;
using Content.Shared.Roles;
using Content.Shared.Radio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class RailroadFactionTest
{
    [Test]
    public async Task SafehousesHaveIndependentAccessAndMatchingSpawns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var department = prototypes.Index<DepartmentPrototype>("Railroad");
            Assert.That(department.Roles.Count, Is.EqualTo(2));
            var houses = new[] { prototypes.Index<DepartmentPrototype>("RailroadSafehouse1"), prototypes.Index<DepartmentPrototype>("RailroadSafehouse2") };
            Assert.That(houses[0].Roles.Count, Is.EqualTo(6));
            Assert.That(houses[1].Roles.Count, Is.EqualTo(6));
            Assert.That(houses[0].UICategory, Is.EqualTo(DepartmentUICategory.MajorFaction));
            Assert.That(houses[1].UICategory, Is.EqualTo(DepartmentUICategory.MajorFaction));
            var station = prototypes.Index<GameMapPrototype>("CommonWealth").Stations["CommonWealth"];
            var jobs = station.StationComponentOverrides.Values.Select(entry => entry.Component).OfType<StationJobsComponent>().Single();
            foreach (var house in new[] { 1, 2 })
            {
                foreach (var rank in new[] { "Intern", "Worker", "Agent", "Runner", "Conductor", "SafehouseOwner" })
                {
                    var id = $"RailroadSafehouse{house}{rank}";
                    var job = prototypes.Index<JobPrototype>(id);
                    Assert.That(job.SetPreference, Is.True);
                    Assert.That(job.Whitelisted, Is.EqualTo(rank is "Agent" or "Conductor" or "SafehouseOwner"));
                    Assert.That(job.Access.Any(access => access.Id == $"RailroadSafehouse{house}"), Is.True);
                    Assert.That(job.Access.Any(access => access.Id == $"RailroadSafehouse{3 - house}"), Is.False);
                    Assert.That(job.Access.Any(access => access.Id == $"RailroadSafehouse{house}Command"), Is.EqualTo(rank is "Conductor" or "SafehouseOwner"));
                    Assert.That(jobs.SetupAvailableJobs.Keys, Does.Contain(id));
                    var marker = entities.SpawnEntity(id + "SpawnPoint", map.GridCoords);
                    Assert.That(entities.GetComponent<SpawnPointComponent>(marker).Job?.ID, Is.EqualTo(id));
                    entities.DeleteEntity(marker);
                }
            }
            var alpha = prototypes.Index<JobPrototype>("RailroadAlphaLeader");
            Assert.That(alpha.Whitelisted && alpha.HideWithoutJobWhitelist, Is.True);
            Assert.That(jobs.SetupAvailableJobs[alpha.ID], Is.EqualTo(new int?[] { 1, 1 }));
            var inspector = prototypes.Index<JobPrototype>("RailroadInspector");
            Assert.That(inspector.SetPreference, Is.True);
            Assert.That(jobs.SetupAvailableJobs[inspector.ID], Is.EqualTo(new int?[] { 1, 1 }));
            Assert.That(inspector.Whitelisted && inspector.HideWithoutJobWhitelist, Is.True);
            prototypes.Index<RadioChannelPrototype>("Railroad");

            var inventory = entities.System<InventorySystem>();
            var spawning = entities.System<StationSpawningSystem>();
            foreach (var id in department.Roles.Concat(houses.SelectMany(house => house.Roles)))
            {
                var job = prototypes.Index(id);
                var gear = prototypes.Index(job.StartingGear!.Value);
                var human = entities.SpawnEntity("MobHuman", map.GridCoords);
                spawning.EquipStartingGear(human, gear);
                spawning.EquipIdCard(human, "Undercover Tester", job, null);
                foreach (var (slot, expected) in gear.Equipment)
                {
                    Assert.That(inventory.TryGetSlotEntity(human, slot, out var worn), Is.True, $"{id}: {slot} did not equip");
                    Assert.That(entities.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype!.ID,
                        Is.EqualTo(expected.Id), $"{id}: wrong item in {slot}");
                    Assert.That(entities.HasComponent<ChameleonClothingComponent>(worn.Value), Is.False, $"{id}: {slot} still disguises");
                }
                Assert.That(inventory.TryGetSlotEntity(human, "id", out var card), Is.True);
                var access = entities.GetComponent<AccessComponent>(card!.Value);
                Assert.That(access.Tags.Select(tag => tag.Id), Is.EquivalentTo(job.Access.Select(tag => tag.Id)));
                Assert.That(inventory.TryGetSlotEntity(human, "ears", out var headset), Is.True);
                var channels = entities.GetComponent<EncryptionKeyHolderComponent>(headset!.Value).Channels;
                Assert.That(channels, Does.Contain("Railroad"));
                Assert.That(channels, Does.Contain("WastelandGlobal"));

                if (id.Id.EndsWith("Agent"))
                {
                    var identity = entities.GetComponent<IdCardComponent>(card.Value);
                    Assert.That(identity.LocalizedJobTitle, Is.EqualTo(job.LocalizedName));
                    Assert.That(entities.GetComponent<MetaDataComponent>(card.Value).EntityPrototype!.ID,
                        Is.EqualTo(id.Id + "ID"));
                    Assert.That(inventory.TryGetSlotEntity(human, "back", out var backpack), Is.True);
                    var stored = entities.GetComponent<StorageComponent>(backpack!.Value).Container.ContainedEntities;
                    var armor = stored.Single(uid => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "RailroadAgentCombatArmor");
                    var pistol = stored.Single(uid => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "RailroadAgentSilencedPistol");
                    var slots = entities.System<ItemSlotsSystem>();
                    Assert.That(entities.GetComponent<MetaDataComponent>(slots.GetItemOrNull(pistol, "gun_magazine")!.Value).EntityPrototype!.ID, Is.EqualTo("N14MagazinePistol12mm"));
                    Assert.That(entities.GetComponent<MetaDataComponent>(slots.GetItemOrNull(pistol, "gun_chamber")!.Value).EntityPrototype!.ID, Is.EqualTo("N14CartridgePistol127"));
                    Assert.That(inventory.TryGetSlotEntity(human, "outerClothing", out var vest), Is.True);
                    Assert.That(entities.GetComponent<MetaDataComponent>(vest!.Value).EntityPrototype!.ID, Is.EqualTo("RailroadVest"));
                    Assert.That(entities.HasComponent<ChameleonClothingComponent>(armor), Is.False);
                    var shape = entities.GetComponent<ItemComponent>(armor).Shape!;
                    Assert.That(shape.Count, Is.EqualTo(1));
                    Assert.That(shape[0].Width + 1, Is.EqualTo(1));
                    Assert.That(shape[0].Height + 1, Is.EqualTo(2));
                    var baseArmor = entities.SpawnEntity("N14ClothingOuterCombatArmor", map.GridCoords);
                    Assert.That(entities.GetComponent<ArmorComponent>(armor).Modifiers.Coefficients,
                        Is.EquivalentTo(entities.GetComponent<ArmorComponent>(baseArmor).Modifiers.Coefficients));
                    entities.DeleteEntity(baseArmor);
                }
                entities.DeleteEntity(human);
            }
        });
        await pair.CleanReturnAsync();
    }
}
