using System.Numerics;
using Content.Shared._ReclamationProject.PowerArmor;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.PowerArmor;

public sealed class JunkhoundPulseOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly SharedTransformSystem _transform;
    private readonly SharedContainerSystem _containers;
    private IRenderTexture? _silhouette;
    private ShaderInstance? _outline;
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public JunkhoundPulseOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        _containers = _entities.System<SharedContainerSystem>();
    }

    public IEnumerable<EntityUid> HighlightedItems(EntityUid wearer)
    {
        if (!_entities.TryGetComponent<JunkhoundPulseComponent>(wearer, out var pulse) || pulse.Until <= _timing.CurTime)
            yield break;

        foreach (var item in pulse.Hits)
            if (_entities.EntityExists(item) && !_containers.IsEntityInContainer(item))
                yield return item;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_players.LocalEntity is not {} wearer || args.ViewportControl is not {} viewport ||
            args.Viewport.Eye is not {} eye || !_entities.TryGetComponent<JunkhoundPulseComponent>(wearer, out var pulse) ||
            pulse.Until <= _timing.CurTime || _transform.GetMapCoordinates(wearer).MapId != args.MapId)
            return;

        var origin = _transform.GetWorldPosition(wearer);
        var center = viewport.WorldToScreen(origin);
        var tilePixels = Vector2.Distance(center, viewport.WorldToScreen(origin + Vector2.UnitX));
        var screen = args.ScreenHandle;
        var elapsed = (float) (JunkhoundPulseComponent.Duration - (pulse.Until - _timing.CurTime)).TotalSeconds;
        var sweep = Math.Clamp(elapsed / 0.55f, 0f, 1f);
        if (sweep < 1f)
            screen.DrawCircle(center, tilePixels * JunkhoundPulseComponent.Range * sweep, Color.Aquamarine.WithAlpha(1f - sweep), false);

        if (pulse.Hits.Count == 0) return;
        var bounds = args.ViewportBounds;
        if (_silhouette?.Size != bounds.Size)
        {
            _silhouette?.Dispose();
            _silhouette = _clyde.CreateRenderTarget(bounds.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb, true), name: "Junkhound salvage outlines");
        }

        var map = args.MapId;
        var visible = args.WorldAABB.Enlarged(1f);
        var scale = tilePixels / EyeManager.PixelsPerMeter;
        screen.RenderInRenderTarget(_silhouette, () =>
        {
            foreach (var item in HighlightedItems(wearer))
            {
                if (!_entities.TryGetComponent<SpriteComponent>(item, out var sprite) || !sprite.Visible ||
                    !_entities.TryGetComponent<TransformComponent>(item, out var xform) || xform.MapID != map)
                    continue;
                var position = _transform.GetWorldPosition(xform);
                if (!visible.Contains(position)) continue;
                screen.DrawEntity(item, viewport.WorldToScreen(position) - (Vector2) bounds.TopLeft, new Vector2(scale),
                    null, eye.Rotation, sprite: sprite, xform: xform, xformSystem: _transform);
            }
        }, Color.Transparent);

        _outline ??= _prototypes.Index<ShaderPrototype>("JunkhoundOutline").InstanceUnique();
        _outline.SetParameter("outline_width", MathF.Max(1f, scale));
        screen.UseShader(_outline);
        screen.DrawTextureRect(_silhouette.Texture, bounds);
        screen.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _silhouette?.Dispose();
        _outline?.Dispose();
        base.DisposeBehavior();
    }
}
