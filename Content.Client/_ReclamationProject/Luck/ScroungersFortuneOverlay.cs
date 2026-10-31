using Content.Shared._ReclamationProject.Luck;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._ReclamationProject.Luck;

public sealed class ScroungersFortuneOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    public override void Initialize() => _overlay.AddOverlay(new ScroungersFortuneOverlay(EntityManager, _players));
    public override void Shutdown() => _overlay.RemoveOverlay<ScroungersFortuneOverlay>();
}

public sealed class ScroungersFortuneOverlay(IEntityManager em, IPlayerManager players) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    protected override void Draw(in OverlayDrawArgs args)
    {
        if (players.LocalEntity is not { } uid || !em.TryGetComponent<ScroungersFortuneComponent>(uid, out var search) ||
            !search.Searching || !em.TryGetComponent<TransformComponent>(uid, out var transform) || transform.MapID != args.MapId)
            return;
        var position = em.System<SharedTransformSystem>().GetWorldPosition(transform);
        var color = Color.FromHex("#D9C788");
        args.WorldHandle.DrawCircle(position, ScroungersFortuneComponent.Radius, color.WithAlpha(0.04f));
        args.WorldHandle.DrawCircle(position, ScroungersFortuneComponent.Radius, color.WithAlpha(0.45f), filled: false);
    }
}
