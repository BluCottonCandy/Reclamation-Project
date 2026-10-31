using Content.Shared._Misfits.Warcry;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Misfits.Warcry;

/// <summary>
/// Draws persistent world-space circles around active warcry sources.
/// </summary>
public sealed class WarcryOverlay(IEntityManager entityManager) : Overlay
{
    private const float FillAlpha = 0.05f;
    private const float OutlineAlpha = 0.85f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;
        var xformSystem = entityManager.System<SharedTransformSystem>();

        foreach (var (active, xform) in entityManager.EntityQuery<ActiveWarcryComponent, TransformComponent>(true))
        {
            if (xform.MapID != args.MapId)
            {
                continue;
            }

            var position = xformSystem.GetWorldPosition(xform);
            if (!args.WorldAABB.Contains(position))
            {
                continue;
            }

            if (active.SoftEdge)
            {
                // Layer faint discs to shade the center and feather outward to a subtle boundary.
                const int bands = 24;
                for (var i = 0; i < bands; i++)
                {
                    var radius = active.Radius * (1f - i / (float) bands);
                    worldHandle.DrawCircle(position, radius, active.Color.WithAlpha(0.004f));
                }
                worldHandle.DrawCircle(position, active.Radius, active.Color.WithAlpha(0.12f), filled: false);
                continue;
            }

            worldHandle.DrawCircle(position, active.Radius, active.Color.WithAlpha(FillAlpha));
            worldHandle.DrawCircle(position, active.Radius, active.Color.WithAlpha(OutlineAlpha), filled: false);
        }
    }
}