using System.Collections.Generic;
using System.IO;
using Content.Server._Reclamation.Bunkers;
using Content.Server.Botany.Components;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
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
                Assert.That(bunker.IsRoomBelonging(catchId, room.Grid), Is.False, speciesId);
                if (speciesId == species[^1])
                {
                    mobs.ChangeMobState(control, MobState.Dead);
                    Assert.That(bunker.IsRoomBelonging(control, room.Grid), Is.False);
                    bunker.ProcessForcedExit(room, control);
                    Assert.That(em.GetComponent<TransformComponent>(control).MapUid, Is.EqualTo(outside.MapUid));
                    Assert.That(room.SaveQueued, Is.False, "the last catch is still alive");
                }
                em.System<DamageableSystem>().TryChangeDamage(catchId,
                    new DamageSpecifier { DamageDict = { ["Asphyxiation"] = 10 } }, ignoreResistances: true);
                Assert.That(mobs.IsDead(catchId), speciesId);
                if (speciesId == "FishCarp")
                    Assert.That(em.System<SharedContainerSystem>().Insert(catchId, em.GetComponent<StorageComponent>(bag).Container));
                bunker.ProcessForcedExit(room, catchId);
                Assert.That(bunker.IsRoomBelonging(catchId, room.Grid), speciesId);
            }
            Assert.That(room.SaveQueued, "the last catch dying must queue a save for the empty room");

            var (loadedMap, loadedGrid) = ReloadRoom(em, room.Grid);
            try
            {
                var saved = new List<string>();
                var query = em.AllEntityQueryEnumerator<BunkerFishingCatchComponent, TransformComponent>();
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

    private static (EntityUid Map, EntityUid Grid) ReloadRoom(IEntityManager em, EntityUid grid)
    {
        var bunker = em.System<PersonalBunkerSystem>();
        var maps = em.System<SharedMapSystem>();
        var floor = em.GetComponent<MapGridComponent>(grid);
        for (var y = 0; y >= -3; y--)
            maps.SetTile(grid, floor, new Vector2i(1, y), new Tile(1));
        bunker.PrepareRoomContentsForSave(grid, occupied: false);
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
