using System.Linq;
using System.Numerics;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - fire spells leave the floor burning. Short-lived flames (MagicFloorFire) that set anyone
/// standing in them alight. Fire projectiles burn where they land; fire zones and bursts burn the tiles they cover.
/// A "fire spell" is one with the Fire or Ember word that is not mixed with Water or Frost (steam, scald).
/// </summary>
public sealed class MagicFloorFireSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    public const string FloorFire = "MagicFloorFire";
    private const int MaxTiles = 40;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicFloorFireOnHitComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    /// <summary>True for spells that should set the floor alight.</summary>
    public bool IsFireSpell(EntityUid action)
    {
        if (!TryComp<MagicSpellActionComponent>(action, out var spellAction) || spellAction.Spell is not { } id
            || !_proto.TryIndex(id, out var spell))
            return false;
        if (spell.BurnsFloor)
            return true;
        var words = spell.Words.Select(w => w.Id).ToList();
        return (words.Contains("Fire") || words.Contains("Ember")) && !words.Contains("Water") && !words.Contains("Frost");
    }

    private void OnProjectileHit(Entity<MagicFloorFireOnHitComponent> ent, ref ProjectileHitEvent args)
    {
        FireDisc(_transform.GetMapCoordinates(ent), ent.Comp.Radius);
    }

    /// <summary>Sets every tile within <paramref name="radius"/> of a point alight (radius 0 = just that tile).</summary>
    public void FireDisc(MapCoordinates center, float radius, MapCoordinates? spare = null)
    {
        var tiles = new HashSet<Vector2i>();
        var r = (int) MathF.Ceiling(radius);
        var c = new Vector2i((int) MathF.Floor(center.Position.X), (int) MathF.Floor(center.Position.Y));
        for (var x = -r; x <= r; x++)
        {
            for (var y = -r; y <= r; y++)
            {
                if (x * x + y * y <= radius * radius + 0.5f)
                    tiles.Add(c + new Vector2i(x, y));
            }
        }
        Burn(center.MapId, tiles, spare, spare ?? center);
    }

    /// <summary>Sets a cone or line alight, from <paramref name="origin"/> toward <paramref name="click"/>.</summary>
    public void FireShape(MapCoordinates origin, MapCoordinates click, MagicShape shape, float radius, float length)
    {
        if (shape == MagicShape.Circle)
        {
            FireDisc(click, radius);
            return;
        }

        var dir = click.Position - origin.Position;
        if (dir.LengthSquared() < 0.01f)
            dir = new Vector2(0, 1);
        dir = Vector2.Normalize(dir);
        var side = new Vector2(-dir.Y, dir.X);
        var tiles = new HashSet<Vector2i>();
        for (var along = 1f; along <= length; along += 0.5f)
        {
            // A cone widens as it goes (about 45 degrees each side); a line stays narrow.
            var half = shape == MagicShape.Cone ? along * 0.9f : MathF.Max(radius, 0.4f);
            for (var s = -half; s <= half; s += 0.5f)
            {
                var p = origin.Position + dir * along + side * s;
                tiles.Add(new Vector2i((int) MathF.Floor(p.X), (int) MathF.Floor(p.Y)));
            }
        }
        Burn(origin.MapId, tiles, origin, origin);
    }

    /// <summary>
    /// Lights the tiles. Skips walls and other blocked tiles, empty space, and anything the fire's source can't
    /// see (a cone never burns through a wall into the next room).
    /// </summary>
    private bool IsPerson(EntityUid uid) => HasComp<Content.Shared.Mobs.Components.MobStateComponent>(uid);

    private void Burn(MapId map, HashSet<Vector2i> tiles, MapCoordinates? spare, MapCoordinates source)
    {
        Vector2i? spareTile = spare is { } sp
            ? new Vector2i((int) MathF.Floor(sp.Position.X), (int) MathF.Floor(sp.Position.Y))
            : null;
        // Too many tiles (a huge burst): burn an even random scatter of them, not a lopsided chunk.
        var list = tiles.ToList();
        if (list.Count > MaxTiles)
            _random.Shuffle(list);
        var count = 0;
        foreach (var tile in list)
        {
            if (tile == spareTile)
                continue; // never under the caster's own feet
            var spot = new MapCoordinates(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), map);
            if (!_maps.TryFindGridAt(spot, out var gridUid, out var grid))
                continue;
            var indices = _maps.TileIndicesFor(gridUid, grid, spot);
            if (!_maps.TryGetTileRef(gridUid, grid, indices, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;
            if (_turf.IsTileBlocked(gridUid, indices, CollisionGroup.Impassable, grid))
                continue;
            var range = (spot.Position - source.Position).Length() + 0.5f;
            // Walls block the fire; people don't (a body counts as see-through-blocking, so the caster's own
            // body would otherwise block every tile).
            if (!_interaction.InRangeUnobstructed(source, spot, range, CollisionGroup.Opaque, IsPerson))
                continue;
            Spawn(FloorFire, spot);
            if (++count >= MaxTiles)
                break;
        }
    }
}
