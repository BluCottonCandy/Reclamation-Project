// #Misfits Add - Ballistic projectile damage falloff based on travel distance; excludes plasma/laser
using Robust.Shared.Map;

namespace Content.Shared._Misfits.Weapons.Components;

/// <summary>
/// When placed on a ballistic projectile, linearly reduces its damage based on
/// how far it has travelled from its spawn point. Damage is unaffected before
/// <see cref="FalloffStartTiles"/> and reaches a floor of
/// <see cref="MinDamageMultiplier"/> at <see cref="MaxFalloffTiles"/>.
///
/// Larger calibres (e.g. .50 BMG) should be given a large start range and
/// high minimum multiplier so they retain stopping power at range.
/// Smaller pistol-calibre rounds lose energy much faster.
///
/// Laser and plasma projectiles intentionally do NOT carry this component.
/// </summary>
[RegisterComponent]
public sealed partial class BallisticDamageFalloffComponent : Component
{
    /// <summary>
    /// Distance in tiles from the spawn point at which damage falloff begins.
    /// Bullets deal full damage up to this range.
    /// </summary>
    [DataField]
    public float FalloffStartTiles = 4f;

    /// <summary>
    /// Distance in tiles at which damage is reduced to its minimum.
    /// Beyond this range the multiplier is clamped to <see cref="MinDamageMultiplier"/>.
    /// </summary>
    [DataField]
    public float MaxFalloffTiles = 15f;

    /// <summary>
    /// The lowest damage multiplier applied once maximum falloff range is reached.
    /// Value should be between 0.0 and 1.0.
    /// </summary>
    [DataField]
    public float MinDamageMultiplier = 0.5f;

    /// <summary>
    /// Map coordinates recorded automatically when the projectile is first
    /// placed on the map (MapInitEvent). Do not set this in YAML.
    /// </summary>
    public MapCoordinates SpawnPosition = MapCoordinates.Nullspace;
}
