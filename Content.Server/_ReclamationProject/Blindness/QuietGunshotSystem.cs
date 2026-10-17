using Content.Shared._ReclamationProject.Blindness;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Blindness;

/// <summary>Records successful gunfire at a fixed location, without revealing subsequent movement.</summary>
public sealed class QuietGunshotSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transforms = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnShot);
    }

    private void OnShot(EntityUid uid, GunComponent component, ref GunShotEvent args)
    {
        if (!TryComp<QuietAwarenessComponent>(args.User, out var quiet))
            return;
        quiet.LastShotAt = _timing.CurTime;
        quiet.LastShotCoordinates = Transform(args.User).Coordinates;
        quiet.LastShotRotation = _transforms.GetWorldRotation(args.User);
        Dirty(args.User, quiet);
    }
}
