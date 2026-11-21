using System.IO;
using Content.Server._Reclamation.Bunkers;
using Content.Server.Destructible;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class BunkerMaterialLockerTest
{
    [Test]
    public async Task ExistingBenchLinksOnLoadAndDisconnectsWhenMoved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var outside = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var bunker = em.System<PersonalBunkerSystem>();
            var mats = em.System<MaterialStorageSystem>();
            foreach (var grid in new[] { map.Grid, outside.Grid })
                em.System<SharedMapSystem>().SetTile(grid, new Vector2i(1, -3), new Tile(1));
            var bench = em.SpawnEntity("N14WorkbenchArmorbench", map.GridCoords);
            var local = em.GetComponent<MaterialStorageComponent>(bench);
            local.Storage["Cloth"] = 500;
            var locker = bunker.EnsureMaterialLocker(map.Grid);
            Assert.That(mats.TryChangeMaterialAmount(locker, "Cloth", 5900));
            bunker.InitializeRoomLocker(map.Grid);
            Assert.That(local.Storage["Cloth"], Is.EqualTo(400));
            Assert.That(mats.GetMaterialAmount(locker, "Cloth"), Is.EqualTo(6000));
            Assert.That(mats.GetAvailableMaterialAmount(bench, "Cloth"), Is.EqualTo(6400));
            Assert.That(mats.TryConsumeAvailableMaterial(bench, "Cloth", 500));
            Assert.That(mats.GetMaterialAmount(locker, "Cloth"), Is.EqualTo(5900));

            var otherLocker = bunker.EnsureMaterialLocker(outside.Grid);
            em.System<SharedTransformSystem>().SetCoordinates(bench, outside.GridCoords);
            Assert.That(em.GetComponent<MaterialSiloUtilizerComponent>(bench).Silo, Is.EqualTo(otherLocker));
            Assert.That(mats.GetAvailableMaterialAmount(bench, "Cloth"), Is.Zero);
            Assert.That(mats.GetMaterialAmount(locker, "Cloth"), Is.EqualTo(5900));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SaveAndReload(bool occupied)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var em = server.ResolveDependency<IEntityManager>();
            var bunker = em.System<PersonalBunkerSystem>();
            var mats = em.System<MaterialStorageSystem>();
            var maps = em.System<SharedMapSystem>();
            var loader = em.System<MapLoaderSystem>();
            var containers = em.System<SharedContainerSystem>();
            var coords = map.GridCoords;
            for (var x = -2; x <= 2; x++)
            for (var y = -3; y <= 1; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), new Tile(1));

            var ladder = em.SpawnEntity("ReclamationPersonalBunkerExit", new EntityCoordinates(map.Grid.Owner, .5f, -2.5f));
            var locker = bunker.EnsureMaterialLocker(map.Grid);
            Assert.That(bunker.EnsureMaterialLocker(map.Grid), Is.EqualTo(locker));
            var xform = em.GetComponent<TransformComponent>(locker);
            Assert.That(xform.Anchored);
            Assert.That(xform.LocalPosition.X, Is.EqualTo(1.5f));
            Assert.That(xform.LocalPosition.Y, Is.EqualTo(-2.5f));
            Assert.That(em.HasComponent<AnchorableComponent>(locker), Is.False);
            Assert.That(em.HasComponent<DestructibleComponent>(locker), Is.False);
            Assert.That(em.HasComponent<DamageableComponent>(locker), Is.False);

            var user = em.SpawnEntity("MobHuman", coords);
            var stacks = em.System<SharedStackSystem>();
            EntityUid Deposit(string id, int count = 1)
            {
                var item = em.SpawnEntity(id, coords);
                if (em.HasComponent<StackComponent>(item))
                    stacks.SetCount(item, count);
                em.EventBus.RaiseLocalEvent(locker, new InteractUsingEvent(user, item, locker, coords));
                return item;
            }
            Deposit("MaterialCloth1", 3);
            Deposit("MaterialCloth10", 10);
            Deposit("SheetSteel1", 3);
            Deposit("SheetSteel", 30);
            var surplus = Deposit("SheetSteel", 30);
            Assert.That(em.GetComponent<StackComponent>(surplus).Count, Is.EqualTo(3));
            Deposit("N14MaterialLeather1", 2);
            var overflow = Deposit("SheetSteel1");
            Assert.That(em.IsQueuedForDeletion(overflow), Is.False);
            Assert.That(mats.GetMaterialAmount(locker, "Steel"), Is.EqualTo(6000));
            Assert.That(mats.GetMaterialAmount(locker, "Cloth"), Is.EqualTo(1300));
            var leatherAmount = 2 * mats.GetEjectableUnitVolume("Leather");
            Assert.That(mats.GetMaterialAmount(locker, "Leather"), Is.EqualTo(leatherAmount));
            var cable = Deposit("CableApcStack", 30);
            var drawer = em.GetComponent<StorageComponent>(locker).Container;
            Assert.That(drawer.Contains(cable));
            Deposit("CableApcStack", 30);
            Assert.That(drawer.Contains(Deposit("CableApcStack1")), Is.False);
            Assert.That(drawer.ContainedEntities.Sum(item => stacks.GetCount(item)), Is.EqualTo(60));
            Assert.That(drawer.Contains(Deposit("N14CurrencyCap")), Is.False);
            Assert.That(containers.Insert(em.SpawnEntity("SheetSteel1", coords), drawer), Is.False);

            var forge = em.SpawnEntity("N14WorkbenchForge", coords);
            var bench = em.SpawnEntity("N14WorkbenchArmorbench", coords);
            foreach (var machine in new[] { forge, bench })
            {
                Assert.That(em.GetComponent<MaterialSiloUtilizerComponent>(machine).Silo, Is.EqualTo(locker));
                Assert.That(mats.GetMaterialAmount(machine, "Steel"), Is.EqualTo(6000));
            }
            Assert.That(mats.TryChangeMaterialAmount(forge, "Steel", 100), Is.False);
            Deposit("N14MaterialThread1", 2);
            var robes = server.ResolveDependency<IPrototypeManager>().Index<LatheRecipePrototype>("N14ClothingOuterBlackRobes");
            var lathe = em.System<LatheSystem>();
            Assert.That(lathe.TryAddToQueue(bench, robes, actor: user));
            var clothAfterCraft = 1300 - robes.Materials["Cloth"];
            var leatherAfterCraft = leatherAmount - robes.Materials["Leather"];
            Assert.That(mats.GetMaterialAmount(forge, "Cloth"), Is.EqualTo(clothAfterCraft));
            Assert.That(mats.GetMaterialAmount(locker, "Leather"), Is.EqualTo(leatherAfterCraft));

            var moved = new EntityCoordinates(map.Grid.Owner, -1.5f, .5f);
            em.System<SharedTransformSystem>().SetCoordinates(ladder, moved);
            Assert.That(bunker.GetBunkerArrival(map.Grid), Is.EqualTo(moved));
            em.GetComponent<MaterialStorageComponent>(forge).Storage["Steel"] = 180;
            var crate = em.SpawnEntity("CrateGenericSteel", coords);
            var bag = em.SpawnEntity("N14ClothingBackpackMilitary", coords);
            Assert.That(containers.Insert(bag, containers.EnsureContainer<Container>(crate, "test-crate")));
            var bagStorage = em.GetComponent<StorageComponent>(bag).Container;
            var scrap = em.SpawnEntity("N14MaterialThread1", coords);
            var caps = em.SpawnEntity("N14CurrencyCap", coords);
            Assert.That(containers.Insert(scrap, bagStorage));
            Assert.That(containers.Insert(caps, bagStorage));
            var carried = em.SpawnEntity("N14MaterialLeather1", coords);
            Assert.That(containers.Insert(carried, containers.EnsureContainer<Container>(user, "test-inventory")));
            bunker.PrepareRoomContentsForSave(map.Grid, occupied);
            Assert.That(em.EntityExists(overflow), Is.False);
            Assert.That(em.EntityExists(scrap), Is.False);
            Assert.That(em.EntityExists(carried));
            Assert.That(bagStorage.Contains(caps));
            Assert.That(em.GetComponent<MaterialStorageComponent>(forge).Storage, Is.Empty);
            em.DeleteEntity(user);
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid.Owner, writer));
            using var reader = new StringReader(writer.ToString());
            Assert.That(loader.TryLoadGrid(reader, "bunker-locker-test", out var loadedMap, out var loadedGrid,
                new DeserializationOptions { InitializeMaps = true }));
            try
            {
                var saved = bunker.EnsureMaterialLocker(loadedGrid!.Value.Owner);
                Assert.That(bunker.GetBunkerArrival(loadedGrid.Value.Owner).Position, Is.EqualTo(moved.Position));
                Assert.That(mats.GetMaterialAmount(saved, "Steel"), Is.EqualTo(6000));
                Assert.That(mats.GetMaterialAmount(saved, "Cloth"), Is.EqualTo(clothAfterCraft));
                Assert.That(mats.GetMaterialAmount(saved, "Leather"), Is.EqualTo(leatherAfterCraft));
                var children = em.GetComponent<TransformComponent>(loadedGrid.Value.Owner).ChildEnumerator;
                var linked = 0;
                while (children.MoveNext(out var child))
                {
                    if (!em.HasComponent<LatheComponent>(child))
                        continue;
                    Assert.That(em.GetComponent<MaterialSiloUtilizerComponent>(child).Silo, Is.EqualTo(saved));
                    linked++;
                }
                Assert.That(linked, Is.EqualTo(2));
                var savedDrawer = em.GetComponent<StorageComponent>(saved).Container;
                Assert.That(savedDrawer.ContainedEntities.Sum(item => stacks.GetCount(item)), Is.EqualTo(60));
                var stored = savedDrawer.ContainedEntities.First();
                Assert.That(em.GetComponent<StackComponent>(stored).StackTypeId.Id, Is.EqualTo("Cable"));
                var sheet = mats.EjectMaterial(saved, "Steel", 100).Single();
                Assert.That(em.GetComponent<StackComponent>(sheet).Count, Is.EqualTo(1));
                Assert.That(mats.GetMaterialAmount(saved, "Steel"), Is.EqualTo(5900));
                Assert.That(mats.TryChangeMaterialAmount(saved, "Steel", 100), Is.True);
                Assert.That(mats.TryChangeMaterialAmount(saved, "Steel", 1), Is.False);
                Assert.That(containers.Remove(stored, em.GetComponent<StorageComponent>(saved).Container));
                Assert.That(em.EntityExists(stored));
            }
            finally
            {
                em.DeleteEntity(loadedMap?.Owner);
            }
        });
        await pair.CleanReturnAsync();
    }
}
