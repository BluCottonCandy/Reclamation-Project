using Content.Shared._ReclamationProject.Roofs;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Roofs;

/// <summary>
/// Only player-constructed roofs participate. Map roof flags and ordinary walls are not changed.
/// Each cardinally connected section needs one anchored support on, or beside, any of its tiles.
/// </summary>
public sealed class ConstructedRoofSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _nextCheck;
    private static readonly Vector2i[] Neighbors = [new(0, 1), new(1, 0), new(0, -1), new(-1, 0)];

    public override void Initialize()
    {
        SubscribeLocalEvent<ConstructedRoofComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
    }

    private void OnMapInit(EntityUid uid, ConstructedRoofComponent roof, MapInitEvent args)
    {
        if (Transform(uid).GridUid is { } grid)
            EnsureComp<RoofComponent>(grid);
        roof.CollapseAt = _timing.CurTime + roof.SupportGrace;
        _nextCheck = TimeSpan.Zero;
    }

    private bool Locate(EntityUid uid, out EntityUid gridUid, out Vector2i tile)
    {
        var xform = Transform(uid);
        gridUid = xform.GridUid ?? EntityUid.Invalid;
        tile = default;
        if (!TryComp<MapGridComponent>(gridUid, out var grid)) return false;
        tile = _maps.LocalToTile(gridUid, grid, xform.Coordinates);
        return true;
    }

    private bool HasFloor(EntityUid grid, Vector2i tile)
    {
        var floor = _maps.GetTileRef(grid, Comp<MapGridComponent>(grid), tile);
        return !floor.IsSpace() && !floor.GetContentTileDefinition().IsSubFloor;
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        // Prying carpet off another floor must also remove its roof, even when the new floor is solid.
        var removed = new HashSet<Vector2i>();
        foreach (var change in args.Changes)
        {
            if (change.NewTile.TypeId == change.OldTile.TypeId) continue;
            var old = change.OldTile.GetContentTileDefinition();
            var next = change.NewTile.GetContentTileDefinition();
            if (change.NewTile.IsEmpty || next.IsSubFloor || next.ID == old.BaseTurf)
                removed.Add(change.GridIndices);
        }
        if (removed.Count == 0) return;
        var query = EntityQueryEnumerator<ConstructedRoofComponent>();
        while (query.MoveNext(out var uid, out var roof))
        {
            if (Locate(uid, out var grid, out var tile) && grid == args.Entity.Owner && removed.Contains(tile))
                RemoveRoof(uid, roof);
        }
        _nextCheck = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextCheck) return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        CheckSupport();
    }

    public void CheckSupport()
    {
        var roofs = new Dictionary<EntityUid, Dictionary<Vector2i, Entity<ConstructedRoofComponent>>>();
        var query = EntityQueryEnumerator<ConstructedRoofComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var roof, out var metadata))
        {
            if (roof.Removing || metadata.EntityPaused || TerminatingOrDeleted(uid)) continue;
            if (!Locate(uid, out var grid, out var tile)) continue;
            if (!Transform(uid).Anchored || !HasFloor(grid, tile))
            {
                RemoveRoof(uid, roof);
                continue;
            }
            if (!roofs.TryGetValue(grid, out var tiles)) roofs[grid] = tiles = new();
            if (!tiles.TryAdd(tile, (uid, roof))) RemoveRoof(uid, roof);
        }

        var supports = new Dictionary<EntityUid, HashSet<Vector2i>>();
        var supportQuery = EntityQueryEnumerator<RoofSupportComponent>();
        while (supportQuery.MoveNext(out var uid, out _))
        {
            if (TerminatingOrDeleted(uid) || !Transform(uid).Anchored || !Locate(uid, out var grid, out var tile)) continue;
            if (!supports.TryGetValue(grid, out var tiles)) supports[grid] = tiles = new();
            tiles.Add(tile);
        }

        foreach (var (grid, tiles) in roofs)
        {
            supports.TryGetValue(grid, out var beams);
            var visited = new HashSet<Vector2i>();
            foreach (var start in tiles.Keys)
            {
                if (!visited.Add(start)) continue;
                var pending = new Queue<Vector2i>();
                var section = new List<Entity<ConstructedRoofComponent>>();
                var supported = false;
                pending.Enqueue(start);
                while (pending.TryDequeue(out var tile))
                {
                    section.Add(tiles[tile]);
                    supported |= beams?.Contains(tile) == true;
                    foreach (var offset in Neighbors)
                    {
                        var neighbor = tile + offset;
                        supported |= beams?.Contains(neighbor) == true;
                        if (tiles.ContainsKey(neighbor) && visited.Add(neighbor)) pending.Enqueue(neighbor);
                    }
                }
                foreach (var roof in section)
                {
                    if (supported)
                    {
                        roof.Comp.CollapseAt = null;
                        continue;
                    }
                    roof.Comp.CollapseAt ??= _timing.CurTime + roof.Comp.SupportGrace;
                    if (_timing.CurTime >= roof.Comp.CollapseAt) RemoveRoof(roof, roof.Comp);
                }
            }
        }
    }

    private void RemoveRoof(EntityUid uid, ConstructedRoofComponent roof)
    {
        if (roof.Removing || TerminatingOrDeleted(uid)) return;
        roof.Removing = true;
        if (TryComp<IsRoofComponent>(uid, out var shelter))
        {
            shelter.Enabled = false;
            Dirty(uid, shelter);
        }
        var coordinates = Transform(uid).Coordinates;
        Spawn("MaterialWoodPlank1", coordinates);
        Spawn("MaterialWoodPlank1", coordinates);
        QueueDel(uid);
    }
}
