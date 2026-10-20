using System.Linq;
using System.Numerics;
using System.Globalization;
using YamlDotNet.RepresentationModel;
using Content.Client.Items.Systems;
using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Hands;
using Content.Shared.Light.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Client.ResourceManagement;
using Robust.Shared.Utility;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._ReclamationProject.GunAttachments;

/// <summary>
/// Mounts in the gun layer's own pixel coordinates, then follows that layer's current
/// direction, pose and transform. No facing cache or independent held-item rotation.
/// </summary>
public sealed class GunAttachmentVisualSystem : EntitySystem
{
    [Dependency] private readonly ItemSystem _items = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    private const string Root = "_ReclamationProject/Objects/Weapons/GunAttachments/";
    private readonly Dictionary<EntityUid, string> _signatures = new();
    private readonly Dictionary<EntityUid, HeldBinding> _held = new();
    private Dictionary<string, float[][][]> _mounts = new();
    private float _reconcileElapsed;

    private sealed record HeldBinding(EntityUid User, string[] BaseKeys, string[] AttachmentKeys,
        Dictionary<string, (Vector2 Offset, Angle Rotation, Vector2 Scale)> OriginalPoses);

    public readonly record struct Mount(Vector2 Muzzle, Vector2 Rear, Vector2? Stock = null, Vector2? Holo = null)
    {
        public Vector2 Forward => Vector2.Normalize(Muzzle - Rear);
        public Vector2 Down => Forward.X >= 0 ? new(Forward.Y, -Forward.X) : new(-Forward.Y, Forward.X);
        public float Length => Vector2.Distance(Muzzle, Rear);
    }

    public override void Initialize()
    {
        // JSON is a YAML subset. Use the same sandbox-approved reader as action
        // assignments rather than System.Text.Json, which client content cannot call.
        using var reader = _resources.ContentFileReadText(new ResPath("/GunAttachmentMounts.json"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var root = (YamlMappingNode) yaml.Documents[0].RootNode;
        foreach (var (key, value) in root)
        {
            var path = ((YamlScalarNode) key).Value!;
            _mounts[path] = ((YamlSequenceNode) value).Children.Cast<YamlSequenceNode>()
                .Select(direction => direction.Children.Select(frame =>
                    frame is YamlSequenceNode coordinates
                        ? coordinates.Children.Cast<YamlScalarNode>()
                            .Select(number => float.Parse(number.Value!, CultureInfo.InvariantCulture)).ToArray()
                        : Array.Empty<float>()).ToArray()).ToArray();
        }
        SubscribeLocalEvent<GunAttachmentsComponent, GetInhandVisualsEvent>(OnInhand,
            after: new[] { typeof(ItemSystem) });
        SubscribeLocalEvent<GunAttachmentsComponent, HeldVisualsUpdatedEvent>(OnHeldUpdated);
        SubscribeLocalEvent<GunAttachmentsComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, GunAttachmentsComponent component, ComponentShutdown args)
    {
        RestoreHeld(uid);
        _signatures.Remove(uid);
    }

    public override void FrameUpdate(float frameTime)
    {
        _reconcileElapsed += frameTime;
        if (_reconcileElapsed >= .1f)
        {
            _reconcileElapsed = 0;
            var query = EntityQueryEnumerator<GunAttachmentsComponent, SpriteComponent>();
            while (query.MoveNext(out var uid, out var gun, out var sprite))
            {
                var signature = $"{gun.MuzzleSlot.Item}/{gun.GripSlot.Item}/{gun.LightSlot.Item}/{gun.StockSlot.Item}/{gun.LaserSlot.Item}/{gun.HoloSlot.Item}/{gun.BipodDeployed}/{LightOn(gun)}";
                if (!_signatures.TryGetValue(uid, out var old) || old != signature)
                {
                    _signatures[uid] = signature;
                    _items.VisualsChanged(uid);
                }
                var gunLayer = sprite.AllLayers.OfType<SpriteComponent.Layer>()
                    .FirstOrDefault(layer => layer.Visible && HasMount(layer));
                if (gunLayer != null) UpdateLayers(gun, sprite, gunLayer, Angle.Zero, null);
            }
        }
        // Only the equipped weapons require frame-by-frame tracking. World/inventory
        // items reconcile less often and never rebuild a player's hands just for turning.
        foreach (var (uid, binding) in _held)
            if (TryComp<GunAttachmentsComponent>(uid, out var gun)) UpdateHeld(uid, gun);
    }

    private bool HasMount(SpriteComponent.Layer layer) => layer.ActualRsi is { } rsi &&
        _mounts.ContainsKey(ProfileKey(rsi, layer.State.ToString() ?? ""));

    private static string ProfileKey(RSI rsi, string state)
    {
        var path = rsi.Path.ToString().TrimStart('/');
        if (path.StartsWith("Textures/")) path = path["Textures/".Length..];
        return path + "/" + state;
    }

    public bool TryGetMount(SpriteComponent.Layer layer, Angle rotation, out Mount mount, Direction? directionOverride = null)
    {
        mount = default;
        if (layer.ActualRsi is not { } rsi || layer.ActualState is not { } state ||
            !_mounts.TryGetValue(ProfileKey(rsi, layer.State.ToString() ?? ""), out var directions)) return false;
        var direction = SpriteComponent.Layer.GetDirection(state.RsiDirections, rotation.Reduced().FlipPositive());
        if (directionOverride != null) direction = directionOverride.Value.Convert(state.RsiDirections);
        direction = direction.OffsetRsiDir(layer.DirOffset);
        var frames = directions[Math.Min((int) direction, directions.Length - 1)];
        var data = frames[Math.Min(layer.AnimationFrame, frames.Length - 1)];
        if (data == null || data.Length is not (4 or 8)) return false;
        mount = new Mount(new Vector2(data[0], data[1]) / 32f, new Vector2(data[2], data[3]) / 32f,
            data.Length == 8 ? new Vector2(data[4], data[5]) / 32f : null,
            data.Length == 8 ? new Vector2(data[6], data[7]) / 32f : null);
        return mount.Length > 0.03f;
    }

    private bool LightOn(GunAttachmentsComponent gun) => gun.LightSlot.Item is { } light &&
        TryComp<UnpoweredFlashlightComponent>(light, out var flashlight) && flashlight.LightOn;
    public static string Key(int slot) => $"gun-attachment-visual-{slot}";

    private void OnInhand(EntityUid uid, GunAttachmentsComponent gun, GetInhandVisualsEvent args)
    {
        if (gun.BipodDeployed && TryComp<SpriteComponent>(uid, out var weaponSprite))
        {
            var body = weaponSprite.AllLayers.OfType<SpriteComponent.Layer>()
                .FirstOrDefault(layer => layer.Visible && HasMount(layer));
            if (body?.ActualRsi is { } rsi)
            {
                // Held sprites can contain deliberate gaps where the hands/body hide
                // the weapon. A supported gun lying beside the player needs its full
                // body instead, including the magazine layers from the world sprite.
                args.Layers.Clear();
                var index = 0;
                foreach (var layer in weaponSprite.AllLayers.OfType<SpriteComponent.Layer>())
                {
                    if (!layer.Visible || layer.ActualRsi != rsi) continue;
                    var data = layer.ToPrototypeData();
                    data.RsiPath = rsi.Path.ToString();
                    data.MapKeys = null;
                    data.Offset = layer.Offset * .75f;
                    data.Scale = layer.Scale * .75f;
                    args.Layers.Add(($"bipod-weapon-body-{args.Location}-{index++}", data));
                }
            }
        }
        for (var i = 0; i < 6; i++)
        {
            if (Visual(gun, i, new Mount(new Vector2(.3f), new Vector2(-.3f))) is not { } data) continue;
            args.Layers.Add(($"{Key(i)}-{args.Location}", data));
        }
    }

    private void OnHeldUpdated(EntityUid uid, GunAttachmentsComponent gun, HeldVisualsUpdatedEvent args)
    {
        _held.Remove(uid);
        if (!TryComp<SpriteComponent>(args.User, out var sprite)) return;
        var bases = args.RevealedLayers.Where(key => !key.StartsWith("gun-attachment-visual-")).ToArray();
        var attachments = args.RevealedLayers.Where(key => key.StartsWith("gun-attachment-visual-")).ToArray();
        if (bases.Length == 0) return;
        var poses = new Dictionary<string, (Vector2 Offset, Angle Rotation, Vector2 Scale)>();
        foreach (var key in bases)
            if (sprite.LayerMapTryGet(key, out var index) && sprite[index] is SpriteComponent.Layer layer)
                poses[key] = (layer.Offset, layer.Rotation, layer.Scale);
        _held[uid] = new HeldBinding(args.User, bases, attachments, poses);
        UpdateHeld(uid, gun);
    }

    private void RestoreHeld(EntityUid uid)
    {
        if (!_held.Remove(uid, out var binding) || !TryComp<SpriteComponent>(binding.User, out var sprite)) return;
        foreach (var (key, pose) in binding.OriginalPoses)
        {
            if (!sprite.LayerMapTryGet(key, out var index)) continue;
            sprite.LayerSetOffset(index, pose.Offset);
            sprite.LayerSetRotation(index, pose.Rotation);
            sprite.LayerSetScale(index, pose.Scale);
        }
    }

    private void UpdateHeld(EntityUid uid, GunAttachmentsComponent gun)
    {
        if (!_held.TryGetValue(uid, out var binding) || !TryComp<SpriteComponent>(binding.User, out var sprite)) return;
        SpriteComponent.Layer? main = null;
        foreach (var key in binding.BaseKeys)
        {
            if (!sprite.LayerMapTryGet(key, out var index) || sprite[index] is not SpriteComponent.Layer layer) continue;
            if (binding.OriginalPoses.TryGetValue(key, out var original))
            {
                sprite.LayerSetOffset(index, original.Offset);
                sprite.LayerSetRotation(index, original.Rotation);
                sprite.LayerSetScale(index, original.Scale);
            }
            if (layer.Visible && HasMount(layer)) main ??= layer;
        }
        if (main == null) return;
        var rotation = _transform.GetWorldRotation(binding.User) + _eye.CurrentEye.Rotation;
        if (!gun.BipodDeployed && sprite.NoRotation && Math.Abs(sprite.Rotation.Theta) < .001 &&
            TryGetMount(main, rotation, out var standingMount,
                sprite.EnableDirectionOverride ? sprite.DirectionOverride : null))
        {
            var facing = sprite.EnableDirectionOverride
                ? sprite.DirectionOverride.Convert(RsiDirectionType.Dir4)
                : SpriteComponent.Layer.GetDirection(RsiDirectionType.Dir4, rotation.Reduced().FlipPositive());
            var desired = facing == RsiDirection.East ? 1 : facing == RsiDirection.West ? -1 : 0;
            var drawnDirection = SpriteComponent.Layer.GetDirection(main.ActualState!.RsiDirections, rotation.Reduced().FlipPositive());
            main.GetLayerDrawMatrix(drawnDirection, out var standingMatrix);
            var forward = Vector2.TransformNormal(standingMount.Forward, standingMatrix);
            if (desired != 0 && forward.X * desired < 0)
            {
                // Preserve the original held rear anchor instead of reflecting its position
                // across the character, which puts the gun behind the other shoulder.
                var rear = Vector2.Transform(standingMount.Rear, standingMatrix);
                var correction = new Vector2(2 * rear.X, 0);
                foreach (var key in binding.BaseKeys)
                {
                    if (!sprite.LayerMapTryGet(key, out var index) || sprite[index] is not SpriteComponent.Layer layer) continue;
                    sprite.LayerSetOffset(index, layer.Offset * new Vector2(-1, 1) + correction);
                    sprite.LayerSetRotation(index, -layer.Rotation);
                    sprite.LayerSetScale(index, layer.Scale * new Vector2(-1, 1));
                }
            }
        }
        if (gun.BipodDeployed && TryGetMount(main, rotation, out var mount,
                sprite.EnableDirectionOverride ? sprite.DirectionOverride : null))
        {
            var direction = SpriteComponent.Layer.GetDirection(main.ActualState!.RsiDirections, rotation.Reduced().FlipPositive());
            main.GetLayerDrawMatrix(direction, out var matrix);
            var rear = Vector2.Transform(mount.Rear, matrix);
            var forward = Vector2.TransformNormal(mount.Forward, matrix);
            var mirror = sprite.Rotation.RotateVec(Vector2.UnitY).X < 0;
            if (mirror)
            {
                rear.Y = -rear.Y;
                forward.Y = -forward.Y;
            }
            // The prone body's local +Y is its head direction. Aim the rifle along
            // that axis so the body's sideways rotation puts it horizontally beside
            // the player, with its muzzle ahead of their head.
            var turn = Angle.FromDegrees(90) - new Angle(Math.Atan2(forward.Y, forward.X));
            // Keep the rifle below the body on screen on either prone facing.
            var shoulder = new Vector2(mirror ? -.2f : .2f, 0);
            foreach (var key in binding.BaseKeys)
            {
                if (!sprite.LayerMapTryGet(key, out var index) || sprite[index] is not SpriteComponent.Layer layer) continue;
                var offset = layer.Offset;
                if (mirror) offset.Y = -offset.Y;
                sprite.LayerSetOffset(index, shoulder + turn.RotateVec(offset - rear));
                sprite.LayerSetRotation(index, (mirror ? -layer.Rotation : layer.Rotation) + turn);
                if (mirror) sprite.LayerSetScale(index, layer.Scale * new Vector2(1, -1));
            }
        }
        UpdateLayers(gun, sprite, main, rotation, binding.AttachmentKeys);
    }

    private void UpdateLayers(GunAttachmentsComponent gun, SpriteComponent sprite, SpriteComponent.Layer main,
        Angle rotation, string[]? heldKeys)
    {
        if (!TryGetMount(main, rotation, out var mount, sprite.EnableDirectionOverride ? sprite.DirectionOverride : null))
        {
            if (heldKeys != null)
                foreach (var key in heldKeys)
                    if (sprite.LayerMapTryGet(key, out var hidden)) sprite.LayerSetVisible(hidden, false);
            return;
        }
        // Match RenderLayer exactly: the draw matrix uses the unmodified direction,
        // while the selected frame also accounts for direction override/offset.
        var direction = SpriteComponent.Layer.GetDirection(main.ActualState!.RsiDirections, rotation.Reduced().FlipPositive());
        main.GetLayerDrawMatrix(direction, out var matrix);
        for (var i = 0; i < 6; i++)
        {
            var key = heldKeys == null ? Key(i) : heldKeys.FirstOrDefault(k => k.StartsWith(Key(i) + "-"));
            if (key == null) continue;
            var index = sprite.LayerMapReserveBlank(key);
            var data = Visual(gun, i, mount);
            sprite.LayerSetVisible(index, data != null && main.Visible);
            if (data == null) continue;
            // Transform the full basis once; applying a signed scale again would
            // reverse mirrored attachments twice.
            var axis = Vector2.TransformNormal(data.Rotation!.Value.RotateVec(Vector2.UnitX) * data.Scale!.Value.X, matrix);
            var up = Vector2.TransformNormal(data.Rotation.Value.RotateVec(Vector2.UnitY) * data.Scale.Value.Y, matrix);
            var length = axis.Length();
            data.Rotation = new Angle(Math.Atan2(axis.Y, axis.X));
            data.Offset = Vector2.Transform(data.Offset!.Value, matrix);
            data.Scale = new Vector2(length, (axis.X * up.Y - axis.Y * up.X) / length);
            data.RenderingStrategy = main.RenderingStrategy;
            sprite.LayerSetData(index, data);
        }
    }

    /// <summary>Source pivots are measured on the unchanged mounted attachment art, including its transparent padding.</summary>
    private PrototypeLayerData? Visual(GunAttachmentsComponent gun, int slot, Mount mount)
    {
        string path, state;
        Vector2 pivot, target;
        var scale = Math.Clamp(mount.Length / .85f, .45f, 1f);
        var flip = mount.Forward.X < 0 ? -1f : 1f;
        var angle = new Angle(Math.Atan2(mount.Forward.Y, mount.Forward.X));
        switch (slot)
        {
            case 0:
                if (gun.MuzzleSlot.Item is not { } muzzle) return null;
                if (HasComp<GunSuppressorComponent>(muzzle))
                {
                    path = Root + "suppressor.rsi"; state = "suppressor_a";
                    pivot = new Vector2(5, 16); target = mount.Muzzle;
                }
                else
                {
                    path = "_Nuclear14/Objects/Weapons/Melee/combat_knife.rsi"; state = "icon";
                    pivot = new Vector2(11, 22); scale *= .55f;
                    target = mount.Muzzle - mount.Forward / 32f - mount.Down * (2f / 32f);
                    angle -= Angle.FromDegrees(45 * flip);
                }
                break;
            case 1:
                if (gun.GripSlot.Item is not { } grip) return null;
                var bipod = HasComp<GunBipodComponent>(grip);
                path = Root + (bipod ? "bipod.rsi" : "grip.rsi");
                state = bipod ? (gun.BipodDeployed ? "bipod_a-on" : "bipod_a") : "verticalgrip_a";
                pivot = bipod ? new Vector2(gun.BipodDeployed ? 16 : 15, 8) : new Vector2(12, 8);
                target = mount.Muzzle - mount.Forward * (bipod ? 2f / 32f : mount.Length * .25f) + mount.Down / 32f;
                break;
            case 2:
                if (gun.LightSlot.Item == null) return null;
                path = Root + "light.rsi"; state = LightOn(gun) ? "flashlight_a-on" : "flashlight_a";
                pivot = new Vector2(21, 14); target = mount.Muzzle + mount.Down * (.5f / 32f);
                break;
            case 3:
                if (gun.StockSlot.Item == null) return null;
                path = Root + "stock.rsi"; state = "smgstock_a";
                // Seat the compact stock into the rear receiver instead of extending
                // a full-size stock from the last outline pixel.
                scale *= .7f;
                pivot = new Vector2(28, 15); target = mount.Stock ?? mount.Rear + mount.Forward * Math.Max(2f / 32f, mount.Length * .2f);
                break;
            case 4:
                if (gun.LaserSlot.Item == null) return null;
                path = Root + "laser.rsi"; state = "lasersight_a";
                pivot = new Vector2(17, 9); target = mount.Muzzle + mount.Down * (2f / 32f);
                break;
            default:
                if (gun.HoloSlot.Item == null) return null;
                path = Root + "holo.rsi"; state = "reflex_a";
                pivot = new Vector2(16, 16);
                target = mount.Holo ?? mount.Rear + mount.Forward * (mount.Length * .45f) - mount.Down * (2f / 32f);
                break;
        }
        var origin = new Vector2(pivot.X - 16, (16 - pivot.Y) * flip) / 32f;
        return new PrototypeLayerData { RsiPath = path, State = state, Offset = target - angle.RotateVec(origin * scale),
            Rotation = angle, Scale = new Vector2(scale, scale * flip), Visible = true };
    }
}
