using System.Collections.Generic;
using System.IO;
using Content.Server._Reclamation.Bunkers;
using Content.Server.Botany.Components;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC;
using Content.Server.Power.EntitySystems;
using Content.IntegrationTests.Pair;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Interaction;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class BunkerFishingAndBotanyTest
{
    [Test]
    public async Task BubbleCatchesStayThroughDeathAndReload()
    {
        await using var pair = await PoolManager.GetServerClient();
        var interior = await pair.CreateTestMap();
        var outside = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var bunker = em.System<PersonalBunkerSystem>();
            var mobs = em.System<MobStateSystem>();
            var coords = new EntityCoordinates(interior.Grid.Owner, .5f, .5f);
            var hatch = em.SpawnEntity(null, outside.GridCoords);
            var room = new PersonalBunkerSystem.Room("catch-test", interior.MapUid, interior.Grid.Owner, hatch);
            var table = pair.Server.ResolveDependency<IPrototypeManager>().Index<EntityTablePrototype>("WaterFishTable");
            var species = ((GroupSelector) table.Table).Children.Cast<EntSelector>().Select(entry => entry.Id.Id).ToArray();
            var bag = em.SpawnEntity("N14ClothingBackpackMilitary", coords);
            var control = em.SpawnEntity("MobHuman", coords);

            foreach (var speciesId in species)
            {
                var catchId = em.SpawnEntity(speciesId, coords);
                Assert.That(bunker.IsRoomBelonging(catchId, room.Grid), Is.True, speciesId);
                if (speciesId == species[^1])
                {
                    mobs.ChangeMobState(control, MobState.Dead);
                    Assert.That(bunker.IsRoomBelonging(control, room.Grid), Is.False);
                    bunker.ProcessForcedExit(room, control);
                    Assert.That(em.GetComponent<TransformComponent>(control).MapUid, Is.EqualTo(outside.MapUid));
                    Assert.That(room.SaveQueued, "animals alone must let the room save as the last occupant leaves");
                }
                em.System<DamageableSystem>().TryChangeDamage(catchId,
                    new DamageSpecifier { DamageDict = { ["Asphyxiation"] = 10 } }, ignoreResistances: true);
                Assert.That(mobs.IsDead(catchId), speciesId);
                if (speciesId == "FishCarp")
                    Assert.That(em.System<SharedContainerSystem>().Insert(catchId, em.GetComponent<StorageComponent>(bag).Container));
                bunker.ProcessForcedExit(room, catchId);
                Assert.That(bunker.IsRoomBelonging(catchId, room.Grid), speciesId);
            }

            var (loadedMap, loadedGrid) = ReloadRoom(em, room.Grid);
            try
            {
                var saved = new List<string>();
                var query = em.AllEntityQueryEnumerator<BunkerAnimalComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.MapUid != loadedMap) continue;
                    var id = em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID;
                    Assert.That(mobs.IsDead(uid), id);
                    Assert.That(em.GetComponent<DamageableComponent>(uid).Damage.DamageDict["Asphyxiation"].Int(), Is.EqualTo(10), id);
                    if (id == "FishCarp")
                        Assert.That(em.GetComponent<StorageComponent>(xform.ParentUid).Container.Contains(uid));
                    Assert.That(bunker.IsRoomBelonging(uid, loadedGrid), id);
                    saved.Add(id);
                }
                Assert.That(saved, Is.EquivalentTo(species));
            }
            finally
            {
                em.DeleteEntity(loadedMap);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BunkerReloadKeepsNpcAnimalsAndExcludesPlayerBodiesAndTheirContents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var bunker = em.System<PersonalBunkerSystem>();
            var position = new EntityCoordinates(map.Grid.Owner, .5f, .5f);
            EntityUid Spawn(string id) => em.SpawnEntity(id, position);
            var dog = Spawn("N14MobDogFeral");
            em.System<MetaDataSystem>().SetEntityName(dog, "Biscuit");
            em.GetComponent<HTNComponent>(dog).RootTask = new HTNCompoundTask { Task = "IdleCompound" };
            em.System<DamageableSystem>().TryChangeDamage(dog,
                new DamageSpecifier { DamageDict = { ["Blunt"] = 3 } }, ignoreResistances: true);
            var bodies = em.System<SharedBodySystem>();
            var organs = bodies.GetBodyOrgans(dog).Select(part => em.GetComponent<MetaDataComponent>(part.Id).EntityPrototype!.ID).ToArray();
            Assert.That(organs, Does.Contain("OrganAnimalBrain"));
            var animals = new[] { "N14MobPetCat", "N14MobBrahmin", "N14MobChicken", "PackTamedNightstalker", "FishCarp" };
            foreach (var animal in animals)
                Assert.That(bunker.IsRoomBelonging(Spawn(animal), map.Grid.Owner), Is.True, animal);

            var (ordinaryMap, _) = em.System<MapLoaderSystem>().SerializeEntitiesRecursive([map.Grid.Owner]);
            using var mapText = new StringWriter();
            ordinaryMap.Write(mapText);
            Assert.That(mapText.ToString(), Does.Not.Contain("N14MobDogFeral"), "wildlife is saved only by bunkers");

            var human = Spawn("MobHuman");
            var emptyHuman = Spawn("MobHuman");
            var corpse = Spawn("MobHuman");
            em.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            var deadLivestock = Spawn("N14MobBrahmin");
            em.System<MobStateSystem>().ChangeMobState(deadLivestock, MobState.Dead);
            var previous = Spawn("N14MobPetDog");
            var current = Spawn("N14MobPetRoach");
            var minds = em.System<SharedMindSystem>();
            var mind = minds.CreateMind(null).Owner;
            minds.TransferTo(mind, previous);
            minds.TransferTo(mind, human);
            minds.TransferTo(minds.CreateMind(null).Owner, current);

            var possessed = Spawn("N14MobIguana");
            pair.Server.PlayerMan.SetAttachedEntity(session, possessed);
            Assert.That(bunker.IsRoomBelonging(possessed, map.Grid.Owner), Is.False);
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
            var bag = Spawn("N14ClothingBackpackMilitary");
            Assert.That(em.System<SharedHandsSystem>().TryPickup(emptyHuman, bag));
            var carried = Spawn("FishGold");
            Assert.That(em.System<SharedContainerSystem>().Insert(carried, em.GetComponent<StorageComponent>(bag).Container));
            Assert.That(em.GetComponent<MindContainerComponent>(previous).HasMind, Is.False);
            foreach (var excluded in new[] { human, emptyHuman, corpse, deadLivestock, previous, current, possessed, bag, carried,
                         Spawn("N14MobGhoulFeral"), Spawn("N14MobDeathclawSentient"), Spawn("N14MobDeathclawPlayer") })
                Assert.That(bunker.IsRoomBelonging(excluded, map.Grid.Owner), Is.False, $"{em.ToPrettyString(excluded)}");

            var (loadedMap, _) = ReloadRoom(em, map.Grid.Owner, occupied: true);
            try
            {
                var saved = new List<string>();
                var query = em.AllEntityQueryEnumerator<MobStateComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var mob, out var xform))
                {
                    if (xform.MapUid != loadedMap) continue;
                    var meta = em.GetComponent<MetaDataComponent>(uid);
                    saved.Add(meta.EntityPrototype!.ID);
                    Assert.That(mob.CurrentState, Is.EqualTo(MobState.Alive), meta.EntityName);
                    if (meta.EntityPrototype.ID != "N14MobDogFeral") continue;
                    Assert.That(meta.EntityName, Is.EqualTo("Biscuit"));
                    var ai = em.GetComponent<HTNComponent>(uid);
                    Assert.That(ai.RootTask.Task.Id, Is.EqualTo("IdleCompound"));
                    Assert.That(ai.Blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), Is.EqualTo(uid));
                    Assert.That(em.GetComponent<DamageableComponent>(uid).Damage.DamageDict["Blunt"].Int(), Is.EqualTo(3));
                    Assert.That(bodies.GetBodyOrgans(uid).Select(part => em.GetComponent<MetaDataComponent>(part.Id).EntityPrototype!.ID),
                        Is.EquivalentTo(organs));
                }
                Assert.That(saved, Is.EquivalentTo(animals.Append("N14MobDogFeral")));
            }
            finally
            {
                em.DeleteEntity(loadedMap);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StoredExtractorDispensesSeedsFromStack(bool upgraded)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        EntityUid loadedMap = default;
        EntityUid loadedGrid = default;
        float before = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var coords = new EntityCoordinates(map.Grid.Owner, .5f, .5f);
            var extractor = em.SpawnEntity("N14SeedExtractor", coords);
            if (upgraded)
            {
                var machine = em.GetComponent<MachineComponent>(extractor);
                var manipulators = machine.PartContainer.ContainedEntities.Where(uid =>
                    em.TryGetComponent<MachinePartComponent>(uid, out var p) && p.PartType.Id == "Manipulator").ToArray();
                Assert.That(manipulators, Has.Length.EqualTo(2));
                foreach (var part in manipulators)
                {
                    em.DeleteEntity(part);
                    Assert.That(em.System<SharedContainerSystem>().Insert(em.SpawnEntity("NanoManipulatorStockPart", coords), machine.PartContainer));
                }
                em.System<ConstructionSystem>().RefreshParts(extractor, machine);
            }
            before = em.GetComponent<SeedExtractorComponent>(extractor).SeedAmountMultiplier!.Value;
            Assert.That(before, upgraded ? Is.GreaterThan(1f) : Is.EqualTo(1f));
            (loadedMap, loadedGrid) = ReloadRoom(em, map.Grid.Owner);
        });
        try
        {
            // Give the reloaded self-powered machine its first normal power update.
            await pair.RunTicksSync(1);
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var query = em.AllEntityQueryEnumerator<SeedExtractorComponent, TransformComponent>();
                EntityUid? saved = null;
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.GridUid == loadedGrid)
                        saved = uid;
                }
                Assert.That(saved, Is.Not.Null);
                Assert.That(em.System<PowerNetSystem>().IsPowered(saved!.Value, em), "the restored N14 extractor is self-powered");
                var position = new EntityCoordinates(loadedGrid, .5f, .5f);
                var user = em.SpawnEntity("MobHuman", position);
                var fruit = em.SpawnEntity("N14FloraProduceWildMutfruit", position);
                em.System<SharedStackSystem>().SetCount(fruit, 2);
                em.EventBus.RaiseLocalEvent(saved.Value, new InteractUsingEvent(user, fruit, saved.Value, position));
                Assert.That(em.IsQueuedForDeletion(fruit));
                Assert.That(em.GetComponent<SeedExtractorComponent>(saved.Value).SeedAmountMultiplier, Is.EqualTo(before));
                var count = 0;
                var packets = em.AllEntityQueryEnumerator<SeedComponent, TransformComponent>();
                while (packets.MoveNext(out var uid, out _, out var xform))
                    if (xform.MapUid == loadedMap && em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "N14FloraWildMutfruitClippingSeed")
                        count++;
                Assert.That(count, Is.GreaterThanOrEqualTo(2 * (int) before));
                Assert.That(count % 2, Is.Zero);
            });
        }
        finally
        {
            await pair.Server.WaitPost(() => pair.Server.EntMan.DeleteEntity(loadedMap));
        }
        await pair.CleanReturnAsync();
    }

    private static (EntityUid Map, EntityUid Grid) ReloadRoom(IEntityManager em, EntityUid grid, bool occupied = false)
    {
        var bunker = em.System<PersonalBunkerSystem>();
        var maps = em.System<SharedMapSystem>();
        var floor = em.GetComponent<MapGridComponent>(grid);
        for (var y = 0; y >= -3; y--)
            maps.SetTile(grid, floor, new Vector2i(1, y), new Tile(1));
        bunker.PrepareRoomContentsForSave(grid, occupied);
        var snapshot = bunker.SerializeRoomGrid(grid);
        BunkerSnapshotReferences.Clean(snapshot);
        using var writer = new StringWriter();
        snapshot.Write(writer);
        using var reader = new StringReader(writer.ToString());
        Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(reader, "bunker-catch-and-seed-test", out var map, out var loaded,
            new DeserializationOptions { InitializeMaps = true }));
        bunker.RestoreRoomContents(loaded!.Value.Owner, map!.Value.Owner);
        return (map.Value.Owner, loaded.Value.Owner);
    }
}
