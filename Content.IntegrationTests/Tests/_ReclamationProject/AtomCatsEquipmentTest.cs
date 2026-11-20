using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.MagicMirror;
using Content.Shared.NPC.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage;
using Content.Shared.Weapons.Melee;
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
                var profile = HumanoidCharacterProfile.DefaultWithSpecies();
                var wearer = spawn.SpawnPlayerMob(map.GridCoords, new JobComponent { Prototype = job.ID },
                    profile, null);
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
                var pass = Worn("id");
                Assert.That(Prototype(pass), Is.EqualTo("AtomCatsGaragePass"));
                var id = em.GetComponent<IdCardComponent>(pass);
                Assert.That(id.FullName, Is.EqualTo(profile.Name));
                Assert.That(id.LocalizedJobTitle, Is.EqualTo(job.LocalizedName));
                Assert.That(em.GetComponent<MetaDataComponent>(pass).EntityName, Does.Contain("switchblade ID"));
                Assert.That(em.GetComponent<AccessComponent>(pass).Tags.Any(tag => tag.Id == "AtomCats"));
                var carried = em.GetComponent<StorageComponent>(Worn("back")).Container.ContainedEntities.Select(Prototype).ToArray();
                Assert.That(carried, Does.Contain("AtomCatsCut"));
                Assert.That(carried, Does.Contain("Welder"));
                Assert.That(carried.Count(id => id == "AtomCatsStencilCan"), Is.EqualTo(1), rank);

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

    [Test]
    public async Task SwitchbladeCyclesInHandAndOnlyTheCombCanCutHair()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        await pair.Server.WaitAssertion(() =>
        {
            var owner = em.SpawnEntity("MobHuman", map.GridCoords);
            var customer = em.SpawnEntity("MobHuman", map.GridCoords);
            var pass = em.SpawnEntity("AtomCatsGaragePass", map.GridCoords);
            var scissors = em.GetComponent<MagicMirrorComponent>(em.SpawnEntity("BarberScissors", map.GridCoords));
            var access = em.GetComponent<AccessComponent>(pass);
            var identity = em.GetComponent<IdCardComponent>(pass);
            var hands = em.System<SharedHandsSystem>();
            var interaction = em.System<SharedInteractionSystem>();
            var ui = em.System<SharedUserInterfaceSystem>();
            pair.Server.PlayerMan.SetAttachedEntity(session, owner);
            Assert.That(hands.TryPickup(owner, pass));
            void Cycle(string expected)
            {
                Assert.That(interaction.UseInHandInteraction(owner, pass));
                Assert.That(hands.GetActiveItem(owner), Is.EqualTo(pass));
                Assert.That(em.GetComponent<ItemComponent>(pass).HeldPrefix, Is.EqualTo(expected));
            }
            for (var i = 0; i < 2; i++)
            {
                Assert.That(em.HasComponent<MeleeWeaponComponent>(pass), Is.False);
                Assert.That(em.HasComponent<MagicMirrorComponent>(pass), Is.False);
                Cycle("blade");
                Assert.That(em.System<SharedMeleeWeaponSystem>().TryGetWeapon(owner, out var weapon, out var blade));
                Assert.That(weapon, Is.EqualTo(pass));
                Assert.That(blade!.Damage.DamageDict["Slash"].Int(), Is.EqualTo(25));
                interaction.InteractUsing(owner, pass, customer, map.GridCoords);
                Assert.That(ui.IsUiOpen(pass, MagicMirrorUiKey.Key, owner), Is.False);
                Cycle("comb");
                Assert.That(em.HasComponent<MeleeWeaponComponent>(pass), Is.False);
                var comb = em.GetComponent<MagicMirrorComponent>(pass);
                Assert.That(comb.RequireConfirm, Is.EqualTo(scissors.RequireConfirm));
                Assert.That(comb.SelectSlotTime, Is.EqualTo(scissors.SelectSlotTime));
                interaction.InteractUsing(owner, pass, customer, map.GridCoords);
                Assert.That(ui.IsUiOpen(pass, MagicMirrorUiKey.Key, owner));
                Assert.That(comb.Target, Is.EqualTo(customer));
                Cycle("folded");
                Assert.That(ui.IsUiOpen(pass, MagicMirrorUiKey.Key, owner), Is.False);
                Assert.That(em.GetComponent<AccessComponent>(pass), Is.SameAs(access));
                Assert.That(em.GetComponent<IdCardComponent>(pass), Is.SameAs(identity));
            }
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
        });
        await pair.CleanReturnAsync();
    }
}
