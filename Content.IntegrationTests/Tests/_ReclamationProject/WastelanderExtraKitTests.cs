using System.Collections.Generic;
using System.Linq;
using Content.Server.Storage.Components;
using Content.Server.UndecidedLoadout.Components;
using Content.Shared.Armor;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class WastelanderExtraKitTests
{
    [Test]
    public async Task ExtraKitsSupplyMatchingAmmoAndExactlyOneOrdinaryTwoHandedWeapon()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var selector = em.SpawnEntity("ReclamationWastelanderKitSelector", map.GridCoords);
            Assert.That(em.GetComponent<UndecidedLoadoutBackpackComponent>(selector).PossibleSets, Has.Count.EqualTo(9));
            var doctor = em.SpawnEntity("ReclamationWastelanderKitBackAlleyDoctor", map.GridCoords);
            var supplies = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(doctor).Items);
            Assert.That(supplies.Count(id => id == "N14Stimpak"), Is.EqualTo(2));
            Assert.That(supplies, Has.Count.EqualTo(9));
            Assert.That(supplies, Does.Contain("N14ClothingBeltUtilityFilled"));
            var belt = em.SpawnEntity("N14ClothingBeltUtilityFilled", map.GridCoords);
            Assert.That(em.GetComponent<StorageComponent>(belt).Container.ContainedEntities
                .Select(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID),
                Is.EquivalentTo(new[] { "N14Crowbar", "N14Wrench", "N14Screwdriver", "N14Wirecutter", "N14Welder", "N14Multitool" }));
            var revolver = em.SpawnEntity("N14WeaponRevolver9mm", map.GridCoords);
            var loader = em.SpawnEntity("SpeedLoader9", map.GridCoords);
            var ammo = em.SpawnEntity("MagazineBox9mm", map.GridCoords);
            var round = em.GetComponent<RevolverAmmoProviderComponent>(revolver).FillPrototype;
            Assert.That(em.GetComponent<BallisticAmmoProviderComponent>(loader).Proto, Is.EqualTo(round));
            Assert.That(em.GetComponent<BallisticAmmoProviderComponent>(ammo).Proto, Is.EqualTo(round));

            var allowed = new[] { "N14BaseBallBat", "N14SledgeHammer", "N14FireAxe", "N14GolfClub", "N14WastelandSpear" };
            foreach (var id in allowed)
            {
                var weapon = em.SpawnEntity(id, map.GridCoords);
                Assert.That(em.HasComponent<WieldableComponent>(weapon), Is.True, id);
            }
            var bruiser = em.SpawnEntity("ReclamationWastelanderKitBruiser", map.GridCoords);
            var random = new RobustRandom();
            random.SetSeed(42);
            var seen = new HashSet<string>();
            for (var roll = 0; roll < 100; roll++)
            {
                var contents = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(bruiser).Items, random);
                Assert.That(contents, Has.Count.EqualTo(3));
                Assert.That(contents, Does.Contain("N14Psycho"));
                Assert.That(contents, Does.Contain("N14BuffoutPill"));
                var weapon = contents.Except(new[] { "N14Psycho", "N14BuffoutPill" }).Single();
                Assert.That(allowed, Does.Contain(weapon));
                seen.Add(weapon);
            }
            Assert.That(seen, Is.EquivalentTo(allowed));
            var roller = em.SpawnEntity("ReclamationWastelanderKitHighRoller", map.GridCoords);
            var rollerItems = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(roller).Items);
            Assert.That(rollerItems, Is.EquivalentTo(new[] { "ReclamationWeaponPistol45ColtPristine", "MagazineBox45", "N14ClothingUniformJumpsuitCheckered", "ReclamationClothingOuterReinforcedLeatherVest", "ClothingShoesBootsCowboyWhite", "ClothingHeadHatFedoraGrey" }));
            foreach (var id in supplies.Concat(rollerItems).Distinct())
                Assert.That(prototypes.HasIndex<EntityPrototype>(id), Is.True, id);
            var suit = em.SpawnEntity("ReclamationClothingOuterReinforcedLeatherVest", map.GridCoords);
            var uniform = em.SpawnEntity("N14ClothingUniformJumpsuitCheckered", map.GridCoords);
            Assert.That(em.HasComponent<ArmorComponent>(uniform), Is.False);
            var armor = em.GetComponent<ArmorComponent>(suit);
            foreach (var type in new[] { "Blunt", "Slash", "Piercing" })
                Assert.That((float) armor.Modifiers.Coefficients[type], Is.EqualTo(0.75f));
            var colt = em.SpawnEntity("ReclamationWeaponPistol45ColtPristine", map.GridCoords);
            Assert.That(em.HasComponent<ChamberMagazineAmmoProviderComponent>(colt), Is.True);
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task FarmerBagHoldsEveryClippingAndRejectsOtherItems()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var farmer = em.SpawnEntity("ReclamationWastelanderKitFarmer", map.GridCoords);
            Assert.That(EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(farmer).Items),
                Is.EquivalentTo(new[] { "N14ClothingBeltPlantFilled", "ReclamationClippingBagFilled", "N14SackCompostFull", "ReclamationTrainingManualAdvancedFarming" }));
            var bag = em.SpawnEntity("ReclamationClippingBagFilled", map.GridCoords);
            var stored = em.GetComponent<StorageComponent>(bag).Container.ContainedEntities;
            var clippings = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(p => p.ID.StartsWith("N14") && p.ID.EndsWith("ClippingSeed"))
                .Select(p => p.ID).ToArray();
            Assert.That(clippings, Has.Length.EqualTo(25));
            Assert.That(stored.Select(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID),
                Is.EquivalentTo(clippings));
            var shape = em.GetComponent<ItemComponent>(bag).Shape!;
            Assert.That(shape, Has.Count.EqualTo(1));
            Assert.That(shape[0].Width + 1, Is.EqualTo(2));
            Assert.That(shape[0].Height + 1, Is.EqualTo(2));
            var empty = em.SpawnEntity("ReclamationClippingBag", map.GridCoords);
            var storage = em.System<SharedStorageSystem>();
            var clipping = em.SpawnEntity(clippings[0], map.GridCoords);
            Assert.That(storage.CanInsert(empty, clipping, out _), Is.True);
            foreach (var id in new[] { "N14WeaponRevolver9mm", "N14CompostRotted", "WheatSeeds" })
            {
                var other = em.SpawnEntity(id, map.GridCoords);
                Assert.That(storage.CanInsert(empty, other, out _), Is.False, id);
            }
            var compost = em.SpawnEntity("N14SackCompostFull", map.GridCoords);
            Assert.That(em.System<SharedSolutionContainerSystem>().TryGetSolution(compost, "sack", out _, out var solution), Is.True);
            Assert.That(solution!.Volume.Float(), Is.EqualTo(100f));
            Assert.That(solution.GetTotalPrototypeQuantity("Fertilizer").Float(), Is.EqualTo(100f));
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task MariaMagazineDoesNotCoverTheSlideState()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default;
        var guns = new List<EntityUid>();
        var network = new List<NetEntity>();
        await pair.Server.WaitAssertion(() =>
        {
            user = server.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var id in new[] { "N14WeaponPistolMaria", "ReclamationWeaponPistol45ColtPristine" })
            {
                var gun = server.SpawnEntity(id, map.GridCoords);
                guns.Add(gun);
                network.Add(server.GetNetEntity(gun));
                server.System<SharedGunSystem>().SetBoltClosed(gun, server.GetComponent<ChamberMagazineAmmoProviderComponent>(gun), true, user);
            }
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.ResolveDependency<IEntityManager>();
            foreach (var net in network)
            {
                var gun = client.GetEntity(net);
                var sprite = client.GetComponent<SpriteComponent>(gun);
                Assert.That(sprite.LayerMapTryGet(GunVisualLayers.Mag, out _), Is.False);
                Assert.That(sprite[GunVisualLayers.Base].RsiState.ToString(), Is.EqualTo("base"));
            }
        });
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var gun in guns)
                server.System<SharedGunSystem>().SetBoltClosed(gun, server.GetComponent<ChamberMagazineAmmoProviderComponent>(gun), false, user);
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.ResolveDependency<IEntityManager>();
            foreach (var net in network)
                Assert.That(client.GetComponent<SpriteComponent>(client.GetEntity(net))[GunVisualLayers.Base].RsiState.ToString(), Is.EqualTo("bolt-open"));
        });
        await pair.CleanReturnAsync();
    }
}
