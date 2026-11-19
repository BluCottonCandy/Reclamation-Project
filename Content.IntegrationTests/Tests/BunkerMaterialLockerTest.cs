using System.IO;
using Content.Server._Reclamation.Bunkers;
using Content.Server.Destructible;
using Content.Server.Materials;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class BunkerMaterialLockerTest
{
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

            em.SpawnEntity("ReclamationPersonalBunkerExit", new EntityCoordinates(map.Grid.Owner, .5f, -2.5f));
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
            EntityUid Deposit(string id)
            {
                var item = em.SpawnEntity(id, coords);
                em.EventBus.RaiseLocalEvent(locker, new InteractUsingEvent(user, item, locker, coords));
                return item;
            }
            Deposit("SheetSteel");
            Deposit("N14MaterialLeather");
            var overflow = Deposit("SheetSteel1");
            Assert.That(em.IsQueuedForDeletion(overflow), Is.False);
            Assert.That(mats.GetMaterialAmount(locker, "Steel"), Is.EqualTo(mats.GetSingleStackVolume("Steel")));
            Assert.That(mats.GetMaterialAmount(locker, "Leather"), Is.EqualTo(mats.GetSingleStackVolume("Leather")));
            var cable = Deposit("CableApcStack");
            var drawer = em.GetComponent<StorageComponent>(locker).Container;
            Assert.That(drawer.Contains(cable));
            Assert.That(drawer.Contains(Deposit("CableApcStack1")), Is.False);
            Assert.That(drawer.Contains(Deposit("N14CurrencyCap")), Is.False);
            Assert.That(containers.Insert(em.SpawnEntity("SheetSteel1", coords), drawer), Is.False);

            var forge = em.SpawnEntity("N14WorkbenchForge", coords);
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
            Assert.That(mats.GetMaterialAmount(forge, "Steel"), Is.Zero);
            em.DeleteEntity(user);
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid.Owner, writer));
            using var reader = new StringReader(writer.ToString());
            Assert.That(loader.TryLoadGrid(reader, "bunker-locker-test", out var loadedMap, out var loadedGrid,
                new DeserializationOptions { InitializeMaps = true }));
            try
            {
                var saved = bunker.EnsureMaterialLocker(loadedGrid!.Value.Owner);
                Assert.That(mats.GetMaterialAmount(saved, "Steel"), Is.EqualTo(mats.GetSingleStackVolume("Steel")));
                Assert.That(mats.GetMaterialAmount(saved, "Leather"), Is.EqualTo(mats.GetSingleStackVolume("Leather")));
                var stored = em.GetComponent<StorageComponent>(saved).Container.ContainedEntities.Single();
                Assert.That(em.GetComponent<StackComponent>(stored).StackTypeId.Id, Is.EqualTo("Cable"));
                var sheet = mats.EjectMaterial(saved, "Steel", 100).Single();
                Assert.That(em.GetComponent<StackComponent>(sheet).Count, Is.EqualTo(1));
                Assert.That(mats.GetMaterialAmount(saved, "Steel"), Is.EqualTo(mats.GetSingleStackVolume("Steel") - 100));
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
