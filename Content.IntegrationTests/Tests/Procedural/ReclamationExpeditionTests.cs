using System.Linq;
using Content.Shared._Misfits.Expeditions;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._Misfits.Expeditions;
using Content.Server.Procedural;
using Content.Shared.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Procedural;

[TestFixture]
public sealed class ReclamationExpeditionTests
{
    [TestCase("ReclamationExperiment", 42, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationLavaBrig", 103, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationMineshaft", 77, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationSnowyLabs", 212, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationHaunted", 809, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationExperiment", 0, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationLavaBrig", 1, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationMineshaft", 2, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationSnowyLabs", 3, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationHaunted", 4, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationExperiment", 5, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationLavaBrig", 6, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationMineshaft", 7, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationSnowyLabs", 8, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationHaunted", 9, N14ExpeditionCombatTier.Easy, null)]
    [TestCase("ReclamationExperiment", 42, N14ExpeditionCombatTier.Medium, null)]
    [TestCase("ReclamationExperiment", 42, N14ExpeditionCombatTier.Hard, null)]
    [TestCase("ReclamationLavaBrig", 103, N14ExpeditionCombatTier.Hard, null)]
    [TestCase("ReclamationMineshaft", 77, N14ExpeditionCombatTier.Hard, null)]
    [TestCase("ReclamationSnowyLabs", 212, N14ExpeditionCombatTier.Hard, null)]
    [TestCase("ReclamationHaunted", 809, N14ExpeditionCombatTier.Hard, null)]
    [TestCase("ReclamationExperiment", -1, N14ExpeditionCombatTier.Medium, null)]
    [TestCase("ReclamationExperiment", -1, N14ExpeditionCombatTier.Hard, null)]
    [TestCase("ReclamationExperiment", 42, N14ExpeditionCombatTier.Hard, N14ExpeditionEnemyFamily.SuperMutants)]
    [TestCase("ReclamationExperiment", 42, N14ExpeditionCombatTier.Easy, N14ExpeditionEnemyFamily.Ghouls)]
    public async Task ConvertedRoomsGenerateOnlyWastelandStructures(string config, int seed, N14ExpeditionCombatTier tier, N14ExpeditionEnemyFamily? family)
    {
        if (seed < 0)
            seed = Enumerable.Range(0, 10000).First(value =>
            {
                var plan = ExpeditionEncounterPlan.Create(tier, value, family);
                return plan.AdvancedWeapon != null && plan.AdvancedArmor != null;
            });
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        Entity<MapGridComponent> grid = default;
        Task<Dungeon> generation = default!;
        await pair.Server.WaitPost(() =>
        {
            grid = entities.System<SharedMapSystem>().CreateGridEntity(map.MapId);
            generation = entities.System<DungeonSystem>().GenerateDungeonAsync(
                prototypes.Index<DungeonConfigPrototype>(config), grid, grid.Comp, Vector2i.Zero, seed);
        });

        for (var tick = 0; !generation.IsCompleted && tick < 3000; tick++)
            await pair.RunTicksSync(1);
        Assert.That(generation.IsCompleted, Is.True, "Dungeon generation stalled.");
        var dungeon = await generation;

        await pair.Server.WaitAssertion(() =>
        {
            entities.System<ExpeditionBuildingSystem>().Finish(grid, grid.Comp, dungeon);
            var maps = entities.System<SharedMapSystem>();
            var tiles = maps.GetAllTiles(grid, grid.Comp).ToArray();
            Assert.That(tiles.Length, Is.GreaterThan(100));
            var tileTypes = pair.Server.ResolveDependency<ITileDefinitionManager>();
            foreach (var tile in tiles)
                Assert.That(tileTypes[tile.Tile.TypeId].ID, Is.EqualTo("FloorMS13WoodWide"));

            var count = 0;
            var structures = new Dictionary<Vector2i, string>();
            var query = entities.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var transform))
            {
                if (uid == grid.Owner || transform.GridUid != grid.Owner)
                    continue;
                Assert.That(meta.EntityPrototype?.ID, Is.AnyOf(ExpeditionBuildingSystem.Wall, ExpeditionBuildingSystem.Shell,
                    ExpeditionBuildingSystem.Door, ExpeditionBuildingSystem.Window));
                var position = maps.LocalToTile(grid, grid.Comp, transform.Coordinates);
                Assert.That(structures.TryAdd(position, meta.EntityPrototype!.ID), Is.True, "Overlapping structures.");
                count++;
            }
            Assert.That(count, Is.GreaterThan(10));

            var arrival = entities.System<N14ExpeditionSystem>().FindCentralDungeonFloor(grid, grid.Comp, dungeon);
            var index = maps.LocalToTile(grid, grid.Comp, new EntityCoordinates(grid, arrival));
            Assert.That(dungeon.RoomTiles.Contains(index) || dungeon.CorridorTiles.Contains(index), Is.True);
            Assert.That(maps.GetTileRef(grid, grid.Comp, index).Tile.IsEmpty, Is.False);
            Assert.That(structures.ContainsKey(index), Is.False, "Arrival must be a clear floor, not a closed door or window.");
            var floor = tiles.Select(t => t.GridIndices).ToHashSet();
            var directions = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };
            foreach (var p in floor.Where(p => directions.Any(d => !floor.Contains(p + d))))
                Assert.That(structures.GetValueOrDefault(p), Is.EqualTo(ExpeditionBuildingSystem.Shell));
            var doors = structures.Where(p => p.Value == ExpeditionBuildingSystem.Door).Select(p => p.Key).ToArray();
            foreach (var p in doors)
            {
                Assert.That(doors.Any(q => q != p && System.Math.Abs(q.X - p.X) <= 1 && System.Math.Abs(q.Y - p.Y) <= 1), Is.False);
                Assert.That(directions.Any(d => structures.TryGetValue(p + d, out var support) && support != ExpeditionBuildingSystem.Door), Is.True,
                    "Freestanding door has no wall or window touching it.");
                foreach (var d in directions)
                    if (structures.GetValueOrDefault(p + d * 2) == ExpeditionBuildingSystem.Door)
                        Assert.That(structures.GetValueOrDefault(p + d), Is.EqualTo(ExpeditionBuildingSystem.Wall), "Gap between doors was not filled with brick.");
            }
            foreach (var room in dungeon.Rooms)
                Assert.That(room.Tiles.Concat(room.Exterior).Any(p => structures.GetValueOrDefault(p) is
                    ExpeditionBuildingSystem.Window), Is.True, "Room has no window.");
            var open = floor.Where(p => !structures.TryGetValue(p, out var id) || id == ExpeditionBuildingSystem.Door).ToHashSet();
            var reachable = new HashSet<Vector2i> { index };
            var pending = new Queue<Vector2i>();
            pending.Enqueue(index);
            while (pending.TryDequeue(out var current))
                foreach (var d in directions)
                    if (open.Contains(current + d) && reachable.Add(current + d))
                        pending.Enqueue(current + d);
            Assert.That(reachable.SetEquals(open), Is.True, "Interior areas are disconnected.");
            foreach (var room in dungeon.Rooms)
                Assert.That(room.Tiles.Any(reachable.Contains), Is.True, "Room has no reachable interior.");
            foreach (var room in dungeon.Rooms)
            {
                var facingHall = room.Tiles.Concat(room.Exterior).Distinct().Where(p =>
                    structures.GetValueOrDefault(p) is ExpeditionBuildingSystem.Wall or ExpeditionBuildingSystem.Window &&
                    directions.Any(d => room.Tiles.Contains(p + d) && reachable.Contains(p + d) &&
                        !dungeon.RoomTiles.Contains(p - d) && reachable.Contains(p - d)) &&
                    !directions.Any(d => structures.GetValueOrDefault(p + d) == ExpeditionBuildingSystem.Door &&
                        structures.GetValueOrDefault(p - d) == ExpeditionBuildingSystem.Door)).ToArray();
                Assert.That(facingHall.Count(p => structures[p] == ExpeditionBuildingSystem.Window),
                    Is.GreaterThanOrEqualTo(System.Math.Min(3, facingHall.Length)), "Missing hallway observation windows.");
            }
            var dressing = entities.System<ExpeditionDressingSystem>().Dress(grid, grid.Comp, dungeon, arrival, seed, tier, family);
            var encounter = ExpeditionEncounterPlan.Create(tier, seed, family);
            Assert.That(dressing.Furniture.Count, Is.GreaterThan(0));
            Assert.That(dressing.Lights.Count, Is.GreaterThan(0));
            Assert.That(dressing.Decals, Is.GreaterThan(0));
            Assert.That(dressing.BloodDecals, Is.GreaterThanOrEqualTo(10), "Expedition needs blood across multiple rooms.");
            Assert.That(dressing.Hostiles.Count, Is.EqualTo(encounter.Enemies.Length));
            Assert.That(dressing.Hostiles.Select(uid => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID), Is.EquivalentTo(encounter.Enemies));
            var enemyIds = dressing.Hostiles.Select(uid => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID).Distinct().ToArray();
            var enemyFamilies = new[]
            {
                new[] { "ReclamationExpeditionSuperMutantNPC", "ReclamationExpeditionSuperMutantMelee", "ReclamationExpeditionSuperMutantRanged", "ReclamationExpeditionSuperMutantArmored", "ReclamationExpeditionBehemoth", "ReclamationExpeditionNightkinVeteran", "ReclamationExpeditionNightkinElite", "ReclamationExpeditionMutantLeader" },
                new[] { "N14MobRaiderPsycho", "N14MobRaiderPsychoRanged", "N14MobRaiderHunter", "N14MobRaiderSkrimisher", "N14MobRaiderBoss", "ReclamationExpeditionRaiderLeader" },
                new[] { "N14MobGhoulFeral", "N14MobGhoulFeralReaver", "ReclamationExpeditionGlowingLeader" }
            };
            Assert.That(enemyIds.Length, Is.GreaterThanOrEqualTo(tier == N14ExpeditionCombatTier.Medium && encounter.Family == "Super mutants" ? 1 : 2), "Expedition must contain enemy variants.");
            Assert.That(enemyFamilies.Count(family => enemyIds.All(family.Contains)), Is.EqualTo(1), "Enemy families or faction-specific NPCs must not mix.");
            foreach (var hostile in dressing.Hostiles)
            {
                Assert.That(entities.HasComponent<Content.Server.NPC.HTN.HTNComponent>(hostile), Is.True);
                var p = maps.LocalToTile(grid, grid.Comp, entities.GetComponent<TransformComponent>(hostile).Coordinates);
                Assert.That((p - index).LengthSquared, Is.GreaterThanOrEqualTo(144), "Enemy starts too close to arrival.");
                Assert.That(open.Contains(p), Is.True);
                Assert.That(dressing.Furniture.Any(uid => maps.LocalToTile(grid, grid.Comp,
                    entities.GetComponent<TransformComponent>(uid).Coordinates) == p), Is.False, "Enemy spawned inside furniture.");
            }
            var enemyPositions = dressing.Hostiles.Select(uid => maps.LocalToTile(grid, grid.Comp,
                entities.GetComponent<TransformComponent>(uid).Coordinates)).ToArray();
            Assert.That(enemyPositions.Distinct().Count(), Is.EqualTo(encounter.Enemies.Length));
            foreach (var p in enemyPositions)
                Assert.That(enemyPositions.Where(q => p != q).All(q => (p - q).LengthSquared >= 4), Is.True, "Enemies are clustered together.");
            Assert.That(dungeon.Rooms.Count(room => enemyPositions.Any(room.Tiles.Contains)), Is.GreaterThanOrEqualTo(3), "Enemies must spread across rooms.");
            Assert.That(dressing.Cells.Count, Is.InRange(1, 4), "Each expedition needs one to four usable holding cells.");
            Assert.That(dressing.Rooms.Select(r => r.Use), Is.EquivalentTo(new[] { "Armory", "Medical", "Cafeteria" }));
            Assert.That(dressing.WeaponCounters.Count, Is.EqualTo(4));
            Assert.That(dressing.ArmorCounters.Count, Is.EqualTo(3));
            Assert.That(dressing.WeaponCounters.Count + dressing.ArmorCounters.Count, Is.EqualTo(7));
            var armory = dressing.Rooms.Single(r => r.Use == "Armory");
            Assert.That(dressing.WeaponCounters.All(armory.Tiles.Contains), Is.True);
            Assert.That(dressing.ArmorCounters.All(armory.Tiles.Contains), Is.True);
            var furnished = new HashSet<Vector2i>();
            var beds = 0;
            var lootLockers = 0;
            foreach (var uid in dressing.Furniture)
            {
                var meta = entities.GetComponent<MetaDataComponent>(uid);
                var id = meta.EntityPrototype!.ID;
                Assert.That(id.StartsWith("N14") || id.StartsWith("ReclamationExpedition"), Is.True);
                var p = maps.LocalToTile(grid, grid.Comp, entities.GetComponent<TransformComponent>(uid).Coordinates);
                Assert.That(furnished.Add(p), Is.True, "Furniture overlaps another prop.");
                if (id.StartsWith("N14Bed") || id == "ReclamationExpeditionExaminationBed")
                    beds++;
                if (id is "N14LootClosetGrey1" or "N14LootClosetRusty" or "N14LootClosetCabinetWood")
                {
                    lootLockers++;
                    Assert.That(prototypes.Index<EntityPrototype>(id).Components.ContainsKey("StorageFill"), Is.True);
                    var fill = (Content.Shared.Storage.Components.StorageFillComponent) prototypes.Index<EntityPrototype>(id).Components["StorageFill"].Component;
                    Assert.That(fill.Contents.Count, Is.GreaterThan(20), "Locker must use its normal loot table.");
                }
                if (id == "N14DoorMetalBarSlanted" || id.EndsWith("Trash"))
                    continue;
                Assert.That(dressing.Routes.Contains(p), Is.False, "Furniture blocks a reserved route.");
                open.Remove(p);
            }
            Assert.That(beds, Is.GreaterThan(0));
            Assert.That(lootLockers, Is.GreaterThan(0));
            reachable.Clear();
            reachable.Add(index);
            pending.Enqueue(index);
            while (pending.TryDequeue(out var current))
                foreach (var d in directions)
                    if (open.Contains(current + d) && reachable.Add(current + d))
                        pending.Enqueue(current + d);
            Assert.That(reachable.SetEquals(open), Is.True, "Furnishings block a room or corridor.");
            foreach (var counter in dressing.WeaponCounters.Concat(dressing.ArmorCounters))
                Assert.That(directions.Count(d => open.Contains(counter + d) && !furnished.Contains(counter + d)),
                    Is.GreaterThanOrEqualTo(3), "An armory counter has fewer than three exposed sides.");
            foreach (var cell in dressing.Cells)
            {
                Assert.That(reachable.Contains(cell.Door), Is.True);
                Assert.That(reachable.Contains(cell.Inside), Is.True, "Holding cell interior is blocked.");
                Assert.That(reachable.Contains(cell.Outside), Is.True, "Holding cell entrance is blocked.");
            }
            foreach (var uid in dressing.Lights)
            {
                Assert.That(entities.GetComponent<Robust.Server.GameObjects.PointLightComponent>(uid).Enabled, Is.True);
                var light = entities.GetComponent<Robust.Server.GameObjects.PointLightComponent>(uid);
                Assert.That(light.Offset, Is.EqualTo(System.Numerics.Vector2.Zero), "Light source sits on an occluding tile edge.");
                Assert.That(light.Energy, Is.GreaterThan(0));
                Assert.That(entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID, Is.EqualTo("ReclamationExpeditionOrangeLight"));
                var p = maps.LocalToTile(grid, grid.Comp, entities.GetComponent<TransformComponent>(uid).Coordinates);
                Assert.That(floor.Contains(p) && !structures.ContainsKey(p), Is.True, $"Light at {p} is not inside the building ({structures.GetValueOrDefault(p)}).");
                Assert.That(structures.GetValueOrDefault(p + new Vector2i(0, 1)),
                    Is.AnyOf(ExpeditionBuildingSystem.Wall, ExpeditionBuildingSystem.Shell), "Light is not mounted on brick.");
                Assert.That(furnished.Contains(p), Is.False, "Light source is inside furniture.");
            }
            var props = dressing.Furniture.Select(uid => (Uid: uid, Id: entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID)).ToArray();
            foreach (var id in new[] { "ReclamationExpeditionStove", "ReclamationExpeditionDrinks" })
            {
                var machine = props.Single(p => p.Id == id).Uid;
                Assert.That(entities.GetComponent<TransformComponent>(machine).Anchored, Is.True);
                Assert.That(entities.GetComponent<Content.Shared.Construction.Components.AnchorableComponent>(machine).Flags,
                    Is.EqualTo(Content.Shared.Construction.Components.AnchorableFlags.None));
                Assert.That(entities.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(machine).NeedsPower, Is.False);
            }
            Assert.That(props.Count(p => p.Id == "ReclamationExpeditionMedicalCabinet"), Is.EqualTo(1));
            Assert.That(props.Count(p => p.Id == "ReclamationExpeditionExaminationBed"), Is.GreaterThanOrEqualTo(1));
            var medicalCrate = props.Single(p => p.Id == "ReclamationExpeditionMedicalCabinet").Uid;
            var medicines = entities.GetComponent<Content.Server.Storage.Components.EntityStorageComponent>(medicalCrate).Contents.ContainedEntities;
            Assert.That(medicines.Select(uid => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID),
                Is.EquivalentTo(new[] { "N14Stimpak", "N14Stimpak", "N14Ointment10" }), "Medical crate is not stocked.");
            var armoryLocker = props.Single(p => p.Id == "ReclamationExpeditionArmoryLocker").Uid;
            var equipment = entities.GetComponent<Content.Server.Storage.Components.EntityStorageComponent>(armoryLocker).Contents.ContainedEntities;
            Assert.That(equipment.Count, Is.EqualTo(tier == N14ExpeditionCombatTier.Hard ? 5 : 3));
            var attachmentPool = new[] { "N14CombatKnife", "ReclamationGunSuppressor", "ReclamationGunGrip", "ReclamationGunLight", "ReclamationGunStock", "ReclamationGunLaserSight", "ReclamationGunBipod", "ReclamationGunHoloSight" };
            Assert.That(equipment.Count(uid => attachmentPool.Contains(entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID)),
                Is.EqualTo(tier == N14ExpeditionCombatTier.Hard ? 2 : 0), "Only hard expeditions stock exactly two attachments.");
            var ammoPool = (Content.Server.Spawners.Components.RandomSpawnerComponent)
                prototypes.Index<EntityPrototype>("ReclamationExpeditionAmmoSpawner").Components["RandomSpawner"].Component;
            Assert.That(equipment.Count(uid => ammoPool.Prototypes.Any(id => id.Id ==
                entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID)), Is.EqualTo(3), "Armory locker must contain three ammunition boxes.");
            Assert.That(equipment.Any(uid => entities.HasComponent<Content.Shared.Armor.ArmorComponent>(uid)), Is.False);
            Assert.That(equipment.Any(uid => entities.HasComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(uid)), Is.False);
            Assert.That(props.Count(p => p.Id == "N14ChairStoolBarBlack"), Is.GreaterThanOrEqualTo(1));
            var weaponPool = (Content.Server.Spawners.Components.RandomSpawnerComponent)
                prototypes.Index<EntityPrototype>("ReclamationExpeditionWeaponSpawner").Components["RandomSpawner"].Component;
            Assert.That(weaponPool.Offset, Is.Zero);
            foreach (var id in weaponPool.Prototypes)
                Assert.That(prototypes.Index<EntityPrototype>(id).ID, Is.AnyOf("N14WeaponPistol9mm", "N14WeaponPistol45Colt",
                    "N14WeaponSMG9mm", "N14WeaponRifle556Carbine", "N14WeaponRifle762M14"));
            var armorPool = (Content.Server.Spawners.Components.RandomSpawnerComponent)
                prototypes.Index<EntityPrototype>("ReclamationExpeditionArmorSpawner").Components["RandomSpawner"].Component;
            foreach (var id in armorPool.Prototypes)
            {
                var armor = (Content.Shared.Armor.ArmorComponent) prototypes.Index<EntityPrototype>(id).Components["Armor"].Component;
                Assert.That(armor.Modifiers.Coefficients.Values.All(c => c >= 0.65f), Is.True, $"{id} exceeds 35% protection.");
                Assert.That(armor.Modifiers.FlatReduction.Values.All(c => c <= 0), Is.True);
            }
            // Inspect the actual spawned loot as well as the pool definitions.
            var gunCount = 0;
            var armorCount = 0;
            var displayedMedicines = 0;
            var advancedGuns = 0;
            var advancedArmor = 0;
            var clinic = dressing.Rooms.Single(r => r.Use == "Medical");
            var lootQuery = entities.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (lootQuery.MoveNext(out var uid, out var meta, out var transform))
            {
                if (transform.ParentUid != grid.Owner || meta.EntityPrototype == null ||
                    entities.HasComponent<Content.Server.NPC.HTN.HTNComponent>(uid))
                    continue;
                var p = maps.LocalToTile(grid, grid.Comp, transform.Coordinates);
                if (clinic.Tiles.Contains(p) && meta.EntityPrototype.ID is "N14CleanGauze10" or "N14Stimpak")
                {
                    Assert.That(props.Any(prop => prop.Id == "N14TableCounter" &&
                        maps.LocalToTile(grid, grid.Comp, entities.GetComponent<TransformComponent>(prop.Uid).Coordinates) == p), Is.True);
                    displayedMedicines++;
                }
                if (entities.HasComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(uid))
                {
                    var advanced = meta.EntityPrototype.ID == encounter.AdvancedWeapon;
                    Assert.That(advanced || weaponPool.Prototypes.Any(id => id.Id == meta.EntityPrototype.ID), Is.True, "Unexpected expedition weapon.");
                    if (advanced) advancedGuns++;
                    Assert.That(dressing.WeaponCounters.Contains(p), Is.True, "Weapon is not on an armory counter.");
                    gunCount++;
                }
                if (entities.HasComponent<Content.Shared.Armor.ArmorComponent>(uid))
                {
                    var advanced = meta.EntityPrototype.ID == encounter.AdvancedArmor;
                    Assert.That(advanced || armorPool.Prototypes.Any(id => id.Id == meta.EntityPrototype.ID), Is.True);
                    if (advanced) advancedArmor++;
                    Assert.That(dressing.ArmorCounters.Contains(p), Is.True);
                    armorCount++;
                }
            }
            Assert.That(advancedGuns, Is.EqualTo(encounter.AdvancedWeapon == null ? 0 : 1));
            Assert.That(advancedArmor, Is.EqualTo(encounter.AdvancedArmor == null ? 0 : 1));
            Assert.That(gunCount, Is.EqualTo(dressing.WeaponCounters.Count));
            Assert.That(armorCount, Is.EqualTo(dressing.ArmorCounters.Count));
            Assert.That(displayedMedicines, Is.EqualTo(2), "The clinic's supplies are not visible on its counter.");
            entities.DeleteEntity(grid);
        });
        await pair.CleanReturnAsync();
    }
}
