using System.Numerics;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client.Items.Systems;

/// <summary>Reuses normal hand layers, with a facing-aware upright pose for strong one-handed carriers.</summary>
public sealed class OneHandedUprightItemSystem : EntitySystem
{
    [Dependency] private readonly SharedMultiHandedItemSystem _multiHanded = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    private readonly Dictionary<EntityUid, Binding> _held = new();
    private readonly List<EntityUid> _remove = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OneHandedUprightItemComponent, GetInhandVisualsEvent>(OnVisuals, after: new[] { typeof(ItemSystem) });
        SubscribeLocalEvent<OneHandedUprightItemComponent, HeldVisualsUpdatedEvent>(OnUpdated);
        SubscribeLocalEvent<OneHandedUprightItemComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<OneHandedUprightItemComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnVisuals(EntityUid uid, OneHandedUprightItemComponent comp, GetInhandVisualsEvent args)
    {
        if (TryComp<Content.Shared.Wieldable.Components.WieldableComponent>(uid, out var wieldable) &&
            (wieldable.Wielded || TryComp<ItemComponent>(uid, out var item) && item.HeldPrefix == wieldable.WieldedInhandPrefix))
            return;

        if (!TryComp<MultiHandedItemComponent>(uid, out var multi) || _multiHanded.GetHandsNeeded(args.User, multi) != 1)
            return;

        var key = $"upright-one-handed-{uid}-{args.Location}";
        var offset = Offset(args.User, args.Location);
        args.Layers.Clear();
        args.Layers.Add((key, new PrototypeLayerData
        {
            RsiPath = comp.Sprite,
            State = comp.State,
            Rotation = Angle.FromDegrees(comp.Rotation),
            Scale = new Vector2(comp.Scale),
            Offset = offset
        }));
        _held[uid] = new Binding(args.User, args.Location, key, offset);
    }

    private void OnUpdated(EntityUid uid, OneHandedUprightItemComponent comp, HeldVisualsUpdatedEvent args)
    {
        if (_held.TryGetValue(uid, out var binding) && !args.RevealedLayers.Contains(binding.Key))
            _held.Remove(uid);
    }

    private void OnUnequipped(EntityUid uid, OneHandedUprightItemComponent comp, GotUnequippedHandEvent args) => _held.Remove(uid);
    private void OnShutdown(EntityUid uid, OneHandedUprightItemComponent comp, ComponentShutdown args) => _held.Remove(uid);

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _remove.Clear();
        foreach (var (item, binding) in _held)
        {
            if (!TryComp<SpriteComponent>(binding.User, out var sprite) ||
                !TryComp<TransformComponent>(item, out var transform) || transform.ParentUid != binding.User ||
                !sprite.LayerMapTryGet(binding.Key, out var index))
            {
                _remove.Add(item);
                continue;
            }
            var offset = Offset(binding.User, binding.Hand);
            if (offset == binding.Offset)
                continue;
            sprite.LayerSetOffset(index, offset);
            binding.Offset = offset;
        }
        foreach (var item in _remove)
            _held.Remove(item);
    }

    private Vector2 Offset(EntityUid user, HandLocation hand)
    {
        var side = hand == HandLocation.Right ? -1f : 1f;
        var facing = (_transform.GetWorldRotation(user) + _eye.CurrentEye.Rotation).GetCardinalDir();
        var x = facing switch
        {
            Direction.North => -side * 0.28f,
            Direction.East => 0.22f,
            Direction.West => -0.22f,
            _ => side * 0.28f
        };
        return new Vector2(x, 0.30f);
    }

    private sealed class Binding(EntityUid user, HandLocation hand, string key, Vector2 offset)
    {
        public readonly EntityUid User = user;
        public readonly HandLocation Hand = hand;
        public readonly string Key = key;
        public Vector2 Offset = offset;
    }
}
