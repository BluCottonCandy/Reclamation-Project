using System.Linq;
using System.Numerics;
using Content.Shared.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Misfits.Expeditions;

/// <summary>Finish converted expedition buildings without exits through their outer shell.</summary>
public sealed class ExpeditionBuildingSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitions = default!;
    private static readonly Vector2i[] Directions = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    public const string Wall = "N14WallBrickSlanted";
    public const string Shell = "N14WallBrickSlantedIndestructible";
    public const string Door = "N14DoorWoodSecure";
    public const string Window = "ReclamationExpeditionWindow";

    public void Finish(EntityUid gridUid, MapGridComponent grid, Dungeon dungeon)
    {
        // Station room atlases can contain space cutouts. These are enclosed
        // terrestrial buildings, so provide continuous floors beneath every room.
        var buildingTiles = dungeon.RoomTiles.Union(dungeon.CorridorTiles)
            .Union(dungeon.RoomExteriorTiles).Union(dungeon.CorridorExteriorTiles);
        var wood = new Tile(_tileDefinitions["FloorMS13WoodWide"].TileId);
        _maps.SetTiles(gridUid, grid, buildingTiles.Select(p => (p, wood)).ToList());
        var floor = _maps.GetAllTiles(gridUid, grid).Select(t => t.GridIndices).ToHashSet();
        // Some station presets join rooms across open space. Add an enclosed
        // connecting passage wherever sealing those edges would split the building.
        while (true)
        {
            var interior = floor.Where(p => Directions.All(d => floor.Contains(p + d))).ToHashSet();
            var joined = new HashSet<Vector2i>();
            var queue = new Queue<Vector2i>();
            var start = interior.OrderBy(p => p.X).ThenBy(p => p.Y).First();
            joined.Add(start);
            queue.Enqueue(start);
            while (queue.TryDequeue(out var p))
                foreach (var d in Directions)
                    if (interior.Contains(p + d) && joined.Add(p + d))
                        queue.Enqueue(p + d);
            if (joined.Count == interior.Count)
                break;
            var target = interior.Except(joined).OrderBy(p => p.X).ThenBy(p => p.Y).First();
            var origin = joined.OrderBy(p => Math.Abs(p.X - target.X) + Math.Abs(p.Y - target.Y))
                .ThenBy(p => p.X).ThenBy(p => p.Y).First();
            var current = origin;
            while (true)
            {
                for (var x = -2; x <= 2; x++)
                    for (var y = -2; y <= 2; y++)
                        floor.Add(current + new Vector2i(x, y));
                if (current == target)
                    break;
                current += current.X != target.X
                    ? new Vector2i(Math.Sign(target.X - current.X), 0)
                    : new Vector2i(0, Math.Sign(target.Y - current.Y));
            }
        }
        _maps.SetTiles(gridUid, grid, floor.Select(p => (p, wood)).ToList());
        var structures = new Dictionary<Vector2i, EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var transform, out var meta))
        {
            if (transform.GridUid != gridUid || meta.EntityPrototype == null)
                continue;
            var pos = _maps.LocalToTile(gridUid, grid, transform.Coordinates);
            if (structures.TryGetValue(pos, out var previous))
                Del(previous);
            structures[pos] = uid;
        }

        void Set(Vector2i pos, string? prototype)
        {
            if (structures.Remove(pos, out var old))
                Del(old);
            if (prototype != null)
                structures[pos] = Spawn(prototype, _maps.GridTileToLocal(gridUid, grid, pos));
        }

        // Every edge, including courtyards and holes in a room atlas, is sealed.
        var shell = floor.Where(t => Directions.Any(d => !floor.Contains(t + d))).ToHashSet();
        foreach (var pos in floor.OrderBy(t => t.X).ThenBy(t => t.Y))
        {
            if (shell.Contains(pos))
                Set(pos, Shell);
            else if (structures.TryGetValue(pos, out var uid) && MetaData(uid).EntityPrototype?.ID != Door)
                Set(pos, Wall);
        }

        bool IsWall(Vector2i p) => structures.TryGetValue(p, out var e) && MetaData(e).EntityPrototype?.ID != Door;
        HashSet<Vector2i> Flood(Vector2i start)
        {
            var result = new HashSet<Vector2i> { start };
            var pending = new Queue<Vector2i>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var current))
                foreach (var d in Directions)
                {
                    var next = current + d;
                    if (floor.Contains(next) && !IsWall(next) && result.Add(next))
                        pending.Enqueue(next);
                }
            return result;
        }

        var protectedWalls = new HashSet<Vector2i>();
        HashSet<Vector2i> reachable = new();
        void RefreshShell()
        {
            var old = shell.ToArray();
            shell.Clear();
            shell.UnionWith(floor.Where(t => Directions.Any(d => !floor.Contains(t + d))));
            foreach (var p in old.Where(p => !shell.Contains(p)))
                if (structures.TryGetValue(p, out var e) && MetaData(e).EntityPrototype?.ID == Shell)
                    Set(p, Wall);
            foreach (var p in shell)
                if (!structures.TryGetValue(p, out var e) || MetaData(e).EntityPrototype?.ID != Shell)
                    Set(p, Shell);
        }
        void RepairConnections()
        {
            // Repair disconnected interior sections through breakable partitions only.
            var open = floor.Where(t => !IsWall(t)).OrderBy(t => t.X).ThenBy(t => t.Y).ToArray();
            if (open.Length == 0)
                throw new InvalidOperationException("Expedition building has no interior floor.");
            var visited = new HashSet<Vector2i>();
            reachable = new();
            foreach (var p in open)
            {
                if (visited.Contains(p))
                    continue;
                var component = Flood(p);
                visited.UnionWith(component);
                if (component.Count > reachable.Count)
                    reachable = component;
            }
            var anchor = reachable.OrderBy(p => p.X).ThenBy(p => p.Y).First();
            foreach (var target in open)
            {
                if (IsWall(target) || reachable.Contains(target))
                    continue;
                var frontier = new PriorityQueue<Vector2i, int>();
                var costs = new Dictionary<Vector2i, int>();
                var previous = new Dictionary<Vector2i, Vector2i>();
                var minX = floor.Min(p => p.X) - 3;
                var maxX = floor.Max(p => p.X) + 3;
                var minY = floor.Min(p => p.Y) - 3;
                var maxY = floor.Max(p => p.Y) + 3;
                foreach (var p in reachable.OrderBy(p => p.X).ThenBy(p => p.Y))
                {
                    costs[p] = 0;
                    frontier.Enqueue(p, 0);
                }
                while (frontier.TryDequeue(out var current, out var cost))
                {
                    if (current == target)
                        break;
                    if (cost != costs[current])
                        continue;
                    foreach (var d in Directions)
                    {
                        var next = current + d;
                        if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY || protectedWalls.Contains(next))
                            continue;
                        var nextCost = cost + (!floor.Contains(next) ? 100 : shell.Contains(next) ? 40 : IsWall(next) ? 20 : 1);
                        if (costs.TryGetValue(next, out var oldCost) && oldCost <= nextCost)
                            continue;
                        costs[next] = nextCost;
                        previous[next] = current;
                        frontier.Enqueue(next, nextCost);
                    }
                }
                if (!costs.ContainsKey(target))
                {
                    // Crossing door rows can leave a tiny void enclosed entirely
                    // by repaired brick. Fill the void instead of creating a trap.
                    foreach (var p in Flood(target))
                        Set(p, Wall);
                    continue;
                }
                var path = new List<Vector2i>();
                for (var p = target; !reachable.Contains(p); p = previous[p])
                    path.Add(p);
                foreach (var p in path)
                {
                    floor.Add(p);
                    foreach (var d in Directions)
                        floor.Add(p + d);
                }
                _maps.SetTiles(gridUid, grid, floor.Select(p => (p, wood)).ToList());
                RefreshShell();
                foreach (var p in path)
                    if (IsWall(p))
                        Set(p, null);
                reachable = Flood(anchor);
            }
        }
        RepairConnections();

        // Keep one door in each touching group. Removing extras leaves the passage open.
        var doors = structures.Where(p => MetaData(p.Value).EntityPrototype?.ID == Door)
            .Select(p => p.Key).OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var kept = new HashSet<Vector2i>();
        foreach (var p in doors)
        {
            if (kept.Any(k => Math.Abs(k.X - p.X) <= 1 && Math.Abs(k.Y - p.Y) <= 1))
                Set(p, null);
            else
                kept.Add(p);
        }

        // Fill one-tile breaks between two doors, rather than leaving a row of
        // doors floating across an open passage. Keep these repairs during routing.
        RemoveUnsupportedDoors();
        var gaps = floor.Where(p => !structures.ContainsKey(p) &&
            Directions.Any(d => IsDoor(p + d) && IsDoor(p - d))).OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        protectedWalls.UnionWith(gaps);
        foreach (var p in gaps)
            Set(p, Wall);
        RepairConnections();

        bool IsDoor(Vector2i p) => structures.TryGetValue(p, out var e) && MetaData(e).EntityPrototype?.ID == Door;
        void RemoveUnsupportedDoors()
        {
            foreach (var p in structures.Keys.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray())
                if (IsDoor(p) && !Directions.Any(d => IsWall(p + d)))
                    Set(p, null);
        }
        RemoveUnsupportedDoors();

        foreach (var room in dungeon.Rooms)
        {
            var candidates = room.Tiles.Concat(room.Exterior).Distinct()
                .Where(p => !shell.Contains(p) && !protectedWalls.Contains(p) && IsWall(p) &&
                    Directions.Any(d => room.Tiles.Contains(p + d) && reachable.Contains(p + d)))
                .OrderByDescending(p => Directions.Any(d => reachable.Contains(p + d) && reachable.Contains(p - d)))
                .ThenBy(p => Vector2.DistanceSquared(new Vector2(p.X, p.Y), room.Center))
                .ThenBy(p => p.X).ThenBy(p => p.Y).ToArray();
            if (candidates.Length == 0)
            {
                // Some source rooms only meet the corridor at their doorway. Give
                // these rooms an interior observation partition without blocking it.
                var placed = false;
                foreach (var p in room.Tiles.Where(p => reachable.Contains(p) && !structures.ContainsKey(p) &&
                             !shell.Contains(p) && Directions.All(d => floor.Contains(p + d)))
                             .OrderBy(p => Vector2.DistanceSquared(new Vector2(p.X, p.Y), room.Center))
                             .ThenBy(p => p.X).ThenBy(p => p.Y))
                {
                    var start = reachable.First(q => q != p);
                    Set(p, Window);
                    var remaining = Flood(start);
                    if (remaining.Count == reachable.Count - 1)
                    {
                        reachable = remaining;
                        placed = true;
                        break;
                    }
                    Set(p, null);
                }
                if (!placed)
                    throw new InvalidOperationException($"Expedition room at {room.Center} ({room.Tiles.Count} tiles) has no safe interior window position.");
                continue;
            }
            var hallwayWindows = candidates.Where(p => !protectedWalls.Contains(p) && Directions.Any(d =>
                room.Tiles.Contains(p + d) && reachable.Contains(p + d) &&
                !dungeon.RoomTiles.Contains(p - d) && reachable.Contains(p - d))).ToArray();
            if (hallwayWindows.Length > 0)
            {
                foreach (var pos in hallwayWindows.Take(3))
                    Set(pos, Window);
            }
            else
                Set(candidates[0], Window);
        }
        // A later room's observation partition may change its neighbor's sight
        // line. Finish hallway glazing using the final walkable floor layout.
        foreach (var room in dungeon.Rooms)
        {
            var facingHall = room.Tiles.Concat(room.Exterior).Distinct().Where(p =>
                !shell.Contains(p) && !protectedWalls.Contains(p) && IsWall(p) && Directions.Any(d =>
                    room.Tiles.Contains(p + d) && reachable.Contains(p + d) &&
                    !dungeon.RoomTiles.Contains(p - d) && reachable.Contains(p - d)))
                .OrderBy(p => Vector2.DistanceSquared(new Vector2(p.X, p.Y), room.Center))
                .ThenBy(p => p.X).ThenBy(p => p.Y).Take(3).ToArray();
            foreach (var p in facingHall)
                Set(p, Window);
        }
        RemoveUnsupportedDoors();
    }
}
