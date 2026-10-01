// #Misfits Add - Server system applying per-caliber ballistic damage falloff over travel distance
using Content.Shared._Misfits.Weapons.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Map;

namespace Content.Server._Misfits.Weapons;

/// <summary>
/// Scales ballistic projectile damage based on travel distance from spawn point.
/// Calibre-specific falloff is configured via <see cref="BallisticDamageFalloffComponent"/>.
///
/// Larger calibres (.50 BMG) are configured with a wide full-damage zone and a
/// high minimum multiplier, so they retain stopping power even at long range.
/// Small-bore pistol rounds and shotgun pellets fall off much more sharply.
///
/// Projectiles that are NOT ballistic (laser, plasma) simply do not carry the
/// component and are therefore completely unaffected.
/// </summary>
public sealed class BallisticDamageFalloffSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Record the world position the moment the bullet is placed on a map.
        SubscribeLocalEvent<BallisticDamageFalloffComponent, MapInitEvent>(OnMapInit);

        // Scale damage by distance when the bullet hits something.
        SubscribeLocalEvent<BallisticDamageFalloffComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnMapInit(Entity<BallisticDamageFalloffComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.SpawnPosition = _transform.GetMapCoordinates(ent);
    }

    private void OnProjectileHit(Entity<BallisticDamageFalloffComponent> ent, ref ProjectileHitEvent args)
    {
        var comp = ent.Comp;

        // Guard: no valid spawn position recorded (edge case).
        if (comp.SpawnPosition == MapCoordinates.Nullspace)
            return;

        var currentPos = _transform.GetMapCoordinates(ent);

        // Skip if the bullet somehow crossed to a different map.
        if (currentPos.MapId != comp.SpawnPosition.MapId)
            return;

        var distance = (currentPos.Position - comp.SpawnPosition.Position).Length();

        // Full damage until the falloff start range.
        if (distance <= comp.FalloffStartTiles)
            return;

        var range = comp.MaxFalloffTiles - comp.FalloffStartTiles;
        if (range <= 0f)
            return;

        // Linear interpolation: multiplier = 1.0 at FalloffStartTiles → MinDamageMultiplier at MaxFalloffTiles.
        var fraction = Math.Clamp((distance - comp.FalloffStartTiles) / range, 0f, 1f);
        var multiplier = 1f - fraction * (1f - comp.MinDamageMultiplier);

        args.Damage = args.Damage * multiplier;
    }
}
