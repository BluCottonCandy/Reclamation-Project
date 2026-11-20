using System.Linq;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.NPC.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AtomCatsEquipmentTest
{
    [Test]
    public async Task AllSevenRanksSpawnDressedWithARepairKitAndAssembledArmor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var department = protos.Index<DepartmentPrototype>("AtomCats");
            var ranks = new[] { "Lookout", "Greaser", "Muscle", "Rocker", "HotRod", "Underboss", "Boss" };
            Assert.That(department.Roles.Select(role => role.Id), Is.EquivalentTo(ranks.Select(rank => "AtomCats" + rank)));

            var inventory = em.System<InventorySystem>();
            var slots = em.System<ItemSlotsSystem>();
            var spawn = em.System<StationSpawningSystem>();
            string Prototype(EntityUid item) => em.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID;
            foreach (var rank in ranks)
            {
                var job = protos.Index<JobPrototype>("AtomCats" + rank);
                Assert.That(job.SetPreference, Is.EqualTo(rank is not ("Underboss" or "Boss")));
                var wearer = spawn.SpawnPlayerMob(map.GridCoords, new JobComponent { Prototype = job.ID },
                    HumanoidCharacterProfile.DefaultWithSpecies(), null);
                EntityUid Worn(string slot)
                {
                    Assert.That(inventory.TryGetSlotEntity(wearer, slot, out var item), Is.True, $"{rank}: {slot}");
                    return item!.Value;
                }

                Assert.That(em.GetComponent<NpcFactionMemberComponent>(wearer).Factions.Any(f => f.Id == "AtomCats"));
                Assert.That(em.HasComponent<PowerArmorProficiencyComponent>(wearer));
                Assert.That(Prototype(Worn("jumpsuit")), Is.EqualTo("AtomCatsCoveralls"));
                Assert.That(Prototype(Worn("eyes")), Is.EqualTo("AtomCatsShades"));
                Assert.That(Prototype(Worn("shoes")), Is.EqualTo("AtomCatsBoots"));
                var carried = em.GetComponent<StorageComponent>(Worn("back")).Container.ContainedEntities.Select(Prototype).ToArray();
                Assert.That(carried, Does.Contain("AtomCatsCut"));
                Assert.That(carried, Does.Contain("Welder"));

                var frame = Worn("outerClothing");
                var model = Prototype(frame);
                Assert.That(model, Is.AnyOf("AtomCatsT45Red", "AtomCatsT45Blue"));
                Assert.That(Prototype(Worn("head")), Is.EqualTo(model + "Helmet"));
                var armor = em.GetComponent<ModularPowerArmorComponent>(frame);
                Assert.That(armor.Sealed);
                Assert.That(armor.Rating, Is.EqualTo(40));
                var parts = PowerArmorPlates.Slots.Select(s =>
                    em.GetComponent<PowerArmorPlateComponent>(slots.GetItemOrNull(frame, PowerArmorPlates.Container(s))!.Value)).ToArray();
                Assert.That(parts.All(plate => plate.Model.Id == model));
                Assert.That(parts.Select(plate => plate.Rating), Is.EqualTo(new[] { 4, 10, 2, 4 }));
                Assert.That(parts.Select(plate => plate.HitPoints), Is.EqualTo(new[] { 80, 192, 48, 80 }));
                em.DeleteEntity(wearer);
            }
        });
        await pair.CleanReturnAsync();
    }
}
