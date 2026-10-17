using System.Linq;
using Content.Shared._Misfits.Expeditions;
using System.Numerics;
using Content.Server.Decals;
using Content.Server.Spawners.Components;
using Content.Server.Storage.EntitySystems;
using Robust.Shared.Prototypes;
using Content.Shared.Decals;
using Content.Shared.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Misfits.Expeditions;

public sealed record ExpeditionHoldingCell(Vector2i Door, Vector2i Inside, Vector2i Outside);
public sealed record ExpeditionRoomUse(string Use, HashSet<Vector2i> Tiles);
public sealed record ExpeditionDressingResult(HashSet<Vector2i> Routes, List<EntityUid> Furniture,
    List<EntityUid> Lights, List<ExpeditionHoldingCell> Cells, int Decals,
    List<ExpeditionRoomUse> Rooms, List<Vector2i> WeaponCounters, List<Vector2i> ArmorCounters,
    int BloodDecals, List<EntityUid> Hostiles);

/// <summary>Furnish lived-in room layouts while preserving the building's walking routes.</summary>
public sealed class ExpeditionDressingSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private static readonly Vector2i[] Directions = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    public ExpeditionDressingResult Dress(EntityUid gridUid, MapGridComponent grid, Dungeon dungeon, Vector2 arrival, int seed, N14ExpeditionCombatTier tier = N14ExpeditionCombatTier.Easy, N14ExpeditionEnemyFamily? family = null)
    {
        var random = new Random(seed ^ 0x47A91);
        var encounter = ExpeditionEncounterPlan.Create(tier, seed, family);
        var floor = _maps.GetAllTiles(gridUid, grid).Select(t => t.GridIndices).ToHashSet();
        var blocked = new HashSet<Vector2i>();
        var occupied = new HashSet<Vector2i>();
        var doors = new HashSet<Vector2i>();
        var walls = new HashSet<Vector2i>();
        var furniture = new List<EntityUid>();
        var lights = new List<EntityUid>();
        var cells = new List<ExpeditionHoldingCell>();
        var roomUses = new List<ExpeditionRoomUse>();
        var weaponCounters = new List<Vector2i>();
        var armorCounters = new List<Vector2i>();
        var lightPositions = new List<Vector2i>();
        var query = EntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var transform, out var meta))
        {
            if (transform.GridUid != gridUid || meta.EntityPrototype == null)
                continue;
            var p = _maps.LocalToTile(gridUid, grid, transform.Coordinates);
            occupied.Add(p);
            if (meta.EntityPrototype.ID == ExpeditionBuildingSystem.Door)
                doors.Add(p);
            else
            {
                blocked.Add(p);
                if (meta.EntityPrototype.ID is ExpeditionBuildingSystem.Wall or ExpeditionBuildingSystem.Shell)
                    walls.Add(p);
            }
        }
        var start = _maps.LocalToTile(gridUid, grid, new EntityCoordinates(gridUid, arrival));
        var open = floor.Except(blocked).ToHashSet();
        var routes = new HashSet<Vector2i> { start };
        foreach (var door in doors.Append(start))
            for (var x = -1; x <= 1; x++)
                for (var y = -1; y <= 1; y++)
                    routes.Add(door + new Vector2i(x, y));

        bool CanBlock(IEnumerable<Vector2i> positions)
        {
            var added = positions.ToHashSet();
            if (added.Any(p => !open.Contains(p) || routes.Contains(p) || occupied.Contains(p)))
                return false;
            var reached = new HashSet<Vector2i> { start };
            var pending = new Queue<Vector2i>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var p))
                foreach (var d in Directions)
                    if (open.Contains(p + d) && !added.Contains(p + d) && reached.Add(p + d))
                        pending.Enqueue(p + d);
            return reached.Count == open.Count - added.Count;
        }

        EntityUid Place(string prototype, Vector2i p, bool blocks = true, Angle? angle = null)
        {
            var uid = Spawn(prototype, _maps.GridTileToLocal(gridUid, grid, p));
            if (angle != null)
                _transform.SetLocalRotation(uid, angle.Value);
            furniture.Add(uid);
            occupied.Add(p);
            if (blocks)
            {
                blocked.Add(p);
                open.Remove(p);
            }
            return uid;
        }

        // One to four compact holding cells, only where a large room has a clear
        // wall-side footprint away from its circulation paths.
        var cellTarget = random.Next(1, 5);
        foreach (var room in dungeon.Rooms.OrderByDescending(r => r.Tiles.Count).ThenBy(r => r.Center.X).ThenBy(r => r.Center.Y))
        {
            if (cells.Count >= cellTarget || room.Tiles.Count < 49)
                continue;
            var roomArea = room.Tiles.Union(room.Exterior).ToHashSet();
            var placedCell = false;
            foreach (var origin in room.Tiles.OrderBy(p => p.Y).ThenBy(p => p.X))
            {
              foreach (var (width, height) in new[] { (4, 3), (3, 4) })
              {
                var area = (from x in Enumerable.Range(0, width) from y in Enumerable.Range(0, height)
                    select origin + new Vector2i(x, y)).ToArray();
                if (area.Any(p => !roomArea.Contains(p) || routes.Contains(p) || (occupied.Contains(p) && !walls.Contains(p))) ||
                    !area.Any(p => Directions.Any(d => walls.Contains(p + d))))
                    continue;
                var interior = area.Where(p => p.X > origin.X && p.X < origin.X + width - 1 &&
                    p.Y > origin.Y && p.Y < origin.Y + height - 1).ToArray();
                if (interior.Any(p => !room.Tiles.Contains(p) || !open.Contains(p) || occupied.Contains(p)))
                    continue;
                foreach (var inside in interior)
                foreach (var direction in Directions)
                {
                var door = inside + direction;
                var outside = door + direction;
                if (!area.Contains(door) || interior.Contains(door) || occupied.Contains(door) || !open.Contains(outside) ||
                    area.Contains(outside) || occupied.Contains(outside))
                    continue;
                var bars = area.Except(interior).Where(p => p != door).ToArray();
                var newBars = bars.Where(p => !walls.Contains(p)).ToArray();
                if (!CanBlock(newBars))
                    continue;
                foreach (var p in newBars)
                    Place("N14WallPrisonSlanted", p);
                Place("N14DoorMetalBarSlanted", door, false);
                cells.Add(new ExpeditionHoldingCell(door, inside, outside));
                var bed = interior.First(p => p != inside);
                if (CanBlock(new[] { bed }))
                    Place("N14BedDirty", bed);
                foreach (var p in area)
                    occupied.Add(p);
                routes.Add(door);
                routes.Add(outside);
                placedCell = true;
                break;
                }
                if (placedCell)
                    break;
              }
              if (placedCell)
                  break;
            }
        }

        // Route the living areas around the fixed cells, instead of forcing a
        // cell to fit between arbitrary preselected room-center paths.
        var parents = new Dictionary<Vector2i, Vector2i> { [start] = start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
            foreach (var d in Directions)
                if (open.Contains(current + d) && parents.TryAdd(current + d, current))
                    queue.Enqueue(current + d);
        var goals = dungeon.Rooms.Select(room => room.Tiles.Where(p => parents.ContainsKey(p) && !occupied.Contains(p))
            .OrderBy(p => Vector2.DistanceSquared(new Vector2(p.X, p.Y), room.Center))
            .ThenBy(p => p.X).ThenBy(p => p.Y).FirstOrDefault(start)).ToArray();
        foreach (var target in goals.Concat(doors).Concat(cells.Select(c => c.Door)).Where(parents.ContainsKey))
            for (var p = target; p != start; p = parents[p])
                routes.Add(p);

        EnsureComp<DecalGridComponent>(gridUid);
        var decalCount = 0;
        bool Decal(string id, Vector2i p, Color? color = null)
        {
            if (_decals.TryAddDecal(id, new EntityCoordinates(gridUid, new Vector2(p.X, p.Y)), out _,
                    color, Angle.FromDegrees(random.Next(4) * 90), cleanable: true))
            {
                decalCount++;
                return true;
            }
            return false;
        }

        var roomNumber = 0;
        var bloodCount = 0;
        // Give the three largest usable rooms distinct purposes. Choose them
        // after reserving circulation, so each can accommodate its furniture.
        var specialRooms = dungeon.Rooms.OrderByDescending(r => r.Tiles.Count(p =>
            open.Contains(p) && !occupied.Contains(p) && !routes.Contains(p)))
            .ThenBy(r => r.Center.X).ThenBy(r => r.Center.Y).Take(3).ToArray();
        foreach (var room in dungeon.Rooms.OrderBy(r => r.Center.X).ThenBy(r => r.Center.Y))
        {
            // Stable room uses form bedroom/storage/dining/workroom clusters;
            // randomness only varies furnishings and the small wear details.
            var use = roomNumber++ % 4;
            var available = room.Tiles.Where(p => open.Contains(p) && !occupied.Contains(p) && !routes.Contains(p))
                .OrderByDescending(p => Directions.Any(d => walls.Contains(p + d)))
                .ThenBy(p => p.Y).ThenBy(p => p.X).ToArray();
            Vector2i? AddWallFurniture(string prototype, Vector2i? near = null)
            {
                foreach (var p in available.OrderByDescending(p => Directions.Any(d => walls.Contains(p + d)))
                             .ThenBy(p => near == null ? 0 : (p - near.Value).LengthSquared))
                    if (!occupied.Contains(p) && Directions.Any(d => room.Tiles.Contains(p + d) && open.Contains(p + d)) && CanBlock(new[] { p }))
                    {
                        Place(prototype, p);
                        return p;
                    }
                return null;
            }

            Vector2i? AddArmoryCounter(string prototype)
            {
                foreach (var p in available)
                {
                    var sides = Directions.Select(d => p + d)
                        .Where(q => open.Contains(q) && !occupied.Contains(q) && !doors.Contains(q)).ToArray();
                    if (sides.Length < 3 || !CanBlock(new[] { p }))
                        continue;
                    Place(prototype, p);
                    // Reserve three approaches, including against later counters
                    // and room furnishing, rather than only checking at placement.
                    routes.UnionWith(sides.Take(3));
                    return p;
                }
                return null;
            }

            if (ReferenceEquals(room, specialRooms.ElementAtOrDefault(0)))
            {
                roomUses.Add(new ExpeditionRoomUse("Armory", room.Tiles.ToHashSet()));
                // Spawners have zero offset, keeping each weapon on its counter.
                // The locker holds ammunition only; all armor and weapons stay
                // on exposed tables, with no hidden equipment bypassing the cap.
                var locker = AddWallFurniture("ReclamationExpeditionArmoryLocker");
                if (locker is { } lockerTile)
                {
                    var lockerUid = furniture.Last();
                    var ammunition = (RandomSpawnerComponent) _prototypes.Index<EntityPrototype>("ReclamationExpeditionAmmoSpawner").Components["RandomSpawner"].Component;
                    for (var i = 0; i < 3; i++)
                    {
                        var box = Spawn(ammunition.Prototypes[random.Next(ammunition.Prototypes.Count)], _maps.GridTileToLocal(gridUid, grid, lockerTile));
                        if (!EntityManager.System<EntityStorageSystem>().Insert(box, lockerUid))
                            throw new InvalidOperationException("Could not stock the expedition armory ammunition.");
                    }
                }
                const int guns = 4;
                for (var i = 0; i < 7; i++)
                {
                    var counter = AddArmoryCounter(i % 2 == 0 ? "N14TableCounterMetal" : "N14TableMetalRound");
                    if (counter == null)
                        break;
                    if (i < guns)
                    {
                        weaponCounters.Add(counter.Value);
                        Spawn(i == 0 && encounter.AdvancedWeapon != null ? encounter.AdvancedWeapon : "ReclamationExpeditionWeaponSpawner", _maps.GridTileToLocal(gridUid, grid, counter.Value));
                    }
                    else
                    {
                        armorCounters.Add(counter.Value);
                        Spawn(i == guns && encounter.AdvancedArmor != null ? encounter.AdvancedArmor : "ReclamationExpeditionArmorSpawner", _maps.GridTileToLocal(gridUid, grid, counter.Value));
                    }
                }
            }
            else if (ReferenceEquals(room, specialRooms.ElementAtOrDefault(1)))
            {
                roomUses.Add(new ExpeditionRoomUse("Medical", room.Tiles.ToHashSet()));
                var bed = AddWallFurniture("ReclamationExpeditionExaminationBed");
                AddWallFurniture("ReclamationExpeditionExaminationBed", bed);
                AddWallFurniture("ReclamationExpeditionMedicalCabinet", bed);
                if (AddWallFurniture("N14TableCounter", bed) is { } medicalCounter)
                {
                    Spawn("N14CleanGauze10", _maps.GridTileToLocal(gridUid, grid, medicalCounter));
                    Spawn("N14Stimpak", _maps.GridTileToLocal(gridUid, grid, medicalCounter).Offset(new Vector2(0.2f, 0)));
                }
                AddWallFurniture("N14ChairWood", bed);
                AddWallFurniture("N14Trashbin", bed);
            }
            else if (ReferenceEquals(room, specialRooms.ElementAtOrDefault(2)))
            {
                roomUses.Add(new ExpeditionRoomUse("Cafeteria", room.Tiles.ToHashSet()));
                var stove = AddWallFurniture("ReclamationExpeditionStove");
                AddWallFurniture("ReclamationExpeditionDrinks", stove);
                AddWallFurniture("ReclamationExpeditionPantry", stove);
                AddWallFurniture("N14TableCounter", stove);
                var tables = 0;
                foreach (var table in available.OrderBy(p => Vector2.DistanceSquared(new Vector2(p.X, p.Y), room.Center)))
                {
                    var stool = table + new Vector2i(0, -1);
                    if (!room.Tiles.Contains(stool) || !CanBlock(new[] { table, stool }))
                        continue;
                    Place("N14TableWoodSquare", table);
                    Place("N14ChairStoolBarBlack", stool);
                    Spawn("N14FoodCram", _maps.GridTileToLocal(gridUid, grid, table));
                    if (++tables >= 2)
                        break;
                }
                AddWallFurniture("N14Trashbin", stove);
            }
            else if (use == 0)
            {
                var bed = AddWallFurniture(random.Next(2) == 0 ? "N14BedWood" : "N14BedDirty");
                AddWallFurniture("N14LootClosetCabinetWood", bed);
                if (room.Tiles.Count >= 55)
                    AddWallFurniture("N14BedWoodBunk");
            }
            else if (use == 1)
            {
                var locker = AddWallFurniture("N14LootClosetGrey1");
                AddWallFurniture("N14LootClosetRusty", locker);
                AddWallFurniture("N14Trashbin", locker);
            }
            else
            {
                // Keep chairs paired with a table, facing it, rather than
                // scattering independent furniture props over the floor.
                foreach (var table in available)
                {
                    var chair = table + new Vector2i(0, -1);
                    if (!room.Tiles.Contains(chair) || !CanBlock(new[] { table, chair }))
                        continue;
                    Place(use == 2 ? "N14TableWoodSquare" : "N14TableDeskWoodSmall", table);
                    Place("N14ChairWood", chair, angle: Angle.FromDegrees(180));
                    break;
                }
                AddWallFurniture(use == 2 ? "ReclamationExpeditionPantry" : "N14LootClosetGrey1");
            }

            // Wear gathers along wall edges and beside storage/eating areas.
            var edges = room.Tiles.Where(p => open.Contains(p) && Directions.Any(d => blocked.Contains(p + d)))
                .OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            if (edges.Length > 0)
            {
                for (var i = 0; i < Math.Clamp(room.Tiles.Count / 12, 4, 10); i++)
                    Decal(random.Next(3) == 0 ? "DirtMedium" : "DirtLight", edges[random.Next(edges.Length)], new Color(0.6f, 0.5f, 0.4f, 0.55f));
                Decal(random.Next(2) == 0 ? "ReclamationExpeditionTinLitter" : "ReclamationExpeditionPacketLitter", edges[random.Next(edges.Length)]);
                if (use is 1 or 2 || roomUses.Any(r => r.Use == "Cafeteria" && r.Tiles.SetEquals(room.Tiles)))
                {
                    var litter = edges[random.Next(edges.Length)];
                    Decal(use == 2 ? "ReclamationExpeditionPacketLitter" : "ReclamationExpeditionTinLitter", litter);
                    Decal("ReclamationExpeditionBoxLitter", edges[random.Next(edges.Length)]);
                    var scrap = available.FirstOrDefault(p => !occupied.Contains(p) && !routes.Contains(p));
                    if (available.Contains(scrap) && !occupied.Contains(scrap) && !routes.Contains(scrap))
                        Place(use == 2 ? "N14FoodCramTrash" : "N14FoodTinCanTrash", scrap, false);
                }
                // Several distinct stains per room, varying the existing blood
                // sprites instead of repeating one identical mark per map.
                var bloodTiles = room.Tiles.Where(p => open.Contains(p) && !occupied.Contains(p))
                    .OrderBy(p => p.X).ThenBy(p => p.Y).OrderBy(_ => random.Next())
                    .Take(Math.Clamp(room.Tiles.Count / 24, 2, 4));
                foreach (var p in bloodTiles)
                {
                    if (Decal($"MisfitsBloodFloor{random.Next(1, 8)}", p,
                            Color.FromHex(random.Next(2) == 0 ? "#660000BB" : "#882222AA")))
                        bloodCount++;
                }
            }

            // One fixture normally serves a room; long rooms can receive a
            // second, provided another fixture is not already close by.
            var roomLights = room.Tiles.Concat(room.Exterior).Where(walls.Contains)
                .Where(p => open.Contains(p + new Vector2i(0, -1)) && !occupied.Contains(p + new Vector2i(0, -1)) &&
                    open.Contains(p + new Vector2i(0, -2)) && !doors.Contains(p + new Vector2i(0, -1)))
                .OrderBy(p => Vector2.DistanceSquared(new Vector2(p.X, p.Y), room.Center))
                .ThenBy(p => p.X).ThenBy(p => p.Y);
            var placedLights = 0;
            foreach (var p in roomLights)
            {
                if (lightPositions.Any(q => (p - q).LengthSquared < 36))
                    continue;
                lights.Add(Spawn("ReclamationExpeditionOrangeLight", _maps.GridTileToLocal(gridUid, grid, p + new Vector2i(0, -1))));
                routes.Add(p + new Vector2i(0, -1));
                lightPositions.Add(p);
                if (++placedLights >= (room.Tiles.Count >= 65 ? 2 : 1))
                    break;
            }
        }

        // Light longer corridor stretches with the same spacing, avoiding a
        // fixture on every wall tile or repeated lights between adjacent rooms.
        foreach (var p in walls.Where(p => open.Contains(p + new Vector2i(0, -1)) && !occupied.Contains(p + new Vector2i(0, -1)) &&
                     open.Contains(p + new Vector2i(0, -2)) && !doors.Contains(p + new Vector2i(0, -1)) && !dungeon.RoomTiles.Contains(p + new Vector2i(0, -1)))
                     .OrderBy(p => p.X).ThenBy(p => p.Y))
        {
            if (lightPositions.Any(q => (p - q).LengthSquared < 49))
                continue;
            lights.Add(Spawn("ReclamationExpeditionOrangeLight", _maps.GridTileToLocal(gridUid, grid, p + new Vector2i(0, -1))));
            lightPositions.Add(p);
        }
        // Select one enemy family for the entire expedition. A
        // separate random stream keeps its population independent of decoration.
        var enemyRandom = new Random(seed ^ 0x7717);
        var enemyTypes = encounter.Enemies;
        var hostiles = new List<EntityUid>();
        var enemyPositions = new List<Vector2i>();
        var candidates = open.Where(p => dungeon.RoomTiles.Contains(p) && !occupied.Contains(p) &&
                !doors.Contains(p) && (p - start).LengthSquared >= 144 && !Directions.Any(d => doors.Contains(p + d)))
            .OrderBy(p => p.X).ThenBy(p => p.Y).OrderBy(_ => enemyRandom.Next()).ToArray();
        var enemyTarget = enemyTypes.Length;
        // Cycle through rooms before adding a second occupant to any room.
        // Small layouts can use a two-tile gap after trying the wider spacing.
        foreach (var spacingSquared in new[] { 9, 4 })
        {
            var roomQueues = candidates.GroupBy(p => dungeon.Rooms.FindIndex(r => r.Tiles.Contains(p)))
                .Select(g => new Queue<Vector2i>(g)).ToArray();
            var placed = true;
            while (hostiles.Count < enemyTarget && placed)
            {
                placed = false;
                foreach (var roomQueue in roomQueues)
                {
                    while (roomQueue.TryDequeue(out var p))
                    {
                        if (enemyPositions.Any(q => (p - q).LengthSquared < spacingSquared))
                            continue;
                        hostiles.Add(Spawn(enemyTypes[hostiles.Count % enemyTypes.Length], _maps.GridTileToLocal(gridUid, grid, p)));
                        enemyPositions.Add(p);
                        placed = true;
                        break;
                    }
                    if (hostiles.Count >= enemyTarget)
                        break;
                }
            }
        }
        return new ExpeditionDressingResult(routes, furniture, lights, cells, decalCount, roomUses, weaponCounters, armorCounters, bloodCount, hostiles);
    }
}
