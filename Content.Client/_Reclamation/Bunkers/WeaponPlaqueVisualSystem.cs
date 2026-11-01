using System.Linq;
using Content.Shared._Reclamation.Bunkers;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;

namespace Content.Client._Reclamation.Bunkers;

/// <summary>Keep the actual contained weapon visible above its wall plaque, without changing weapon behavior.</summary>
public sealed class WeaponPlaqueVisualSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    private readonly Dictionary<EntityUid, int> _originalDepths = new();
    private readonly HashSet<EntityUid> _mounted = new();

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _mounted.Clear();
        // Run after container/sprite states arrive, including when a saved plaque enters PVS again.
        var plaques = EntityQueryEnumerator<WeaponPlaqueComponent, SpriteComponent>();
        while (plaques.MoveNext(out var uid, out _, out var plaqueSprite))
        {
            if (!_containers.TryGetContainer(uid, "weapon", out var container)) continue;
            foreach (var weapon in container.ContainedEntities)
            {
                if (!TryComp<SpriteComponent>(weapon, out var sprite)) continue;
                _mounted.Add(weapon);
                _originalDepths.TryAdd(weapon, sprite.DrawDepth);
                if (sprite.DrawDepth != plaqueSprite.DrawDepth + 1)
                    _sprites.SetDrawDepth((weapon, sprite), plaqueSprite.DrawDepth + 1);
            }
        }
        foreach (var (weapon, depth) in _originalDepths.ToArray())
        {
            if (_mounted.Contains(weapon)) continue;
            if (TryComp<SpriteComponent>(weapon, out var sprite))
                _sprites.SetDrawDepth((weapon, sprite), depth);
            _originalDepths.Remove(weapon);
        }
    }
}
