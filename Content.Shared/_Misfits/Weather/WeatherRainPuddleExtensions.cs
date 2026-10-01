// #Misfits Change Add: Rain puddle spawning configuration fields for WeatherPrototype.
// These fields are read by RainPuddleSystem (Content.Server/_Misfits/Weather/) to control
// how—and whether—a given weather prototype deposits water puddles on open outdoor tiles.
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weather;

/// <summary>
/// Partial extension of <see cref="WeatherPrototype"/> that adds rain-puddle spawning fields.
/// Puddle volume is intentionally capped below <see cref="Content.Shared.Fluids.Components.PuddleComponent.OverflowVolume"/>
/// (default 20 u) so that the fluid-spreader system is never triggered and puddles stay put.
/// </summary>
public sealed partial class WeatherPrototype
{
    /// <summary>
    /// If true, the server will periodically spawn/grow water puddles on open tiles
    /// near connected players while this weather is in the <see cref="WeatherState.Running"/> state.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool SpawnPuddles = false;

    /// <summary>
    /// Prototype ID of the reagent deposited by rain.
    /// Defaults to plain water; swap for acid rain, etc.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public ProtoId<ReagentPrototype> PuddleReagent = "Water";

    /// <summary>
    /// Volume (in reagent units) added to a tile's puddle each interval.
    /// Keep well below <see cref="PuddleMaxVolume"/> so tiles fill gradually.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public float PuddleAmountPerInterval = 3f;

    /// <summary>
    /// Seconds between each rain-puddle spawning pass.
    /// 30 s is the default: responsive enough to feel real, cheap enough to run continuously.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public float PuddleInterval = 30f;

    /// <summary>
    /// Maximum puddle volume (units) before a tile is treated as "full" and skipped.
    /// Must stay strictly below <see cref="Content.Shared.Fluids.Components.PuddleComponent.OverflowVolume"/>
    /// (default 20 u) to prevent the spreader from activating and flooding interiors.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public float PuddleMaxVolume = 15f;

    /// <summary>
    /// World-space radius (in tiles / metres) around each player used when
    /// collecting tiles to consider for puddle spawning.
    /// 15 tiles ≈ a typical maximum viewport half-width.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public float PuddleViewportRadius = 15f;

    /// <summary>
    /// Per-tile probability that a puddle is spawned or grown each interval.
    /// 0.25 (25 %) gives an organic, scattered distribution rather than a uniform grid.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public float PuddleChance = 0.25f;

    /// <summary>
    /// If true, rain will gradually wash away non-evaporating reagents (blood, etc.) from
    /// existing puddles on weather-exposed tiles each interval.
    /// Blood's <c>evaporates: false</c> flag means it never dries naturally, so this is the
    /// only way for outdoor blood to be cleaned during a storm.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool WashBloodPuddles = true;

    /// <summary>
    /// Volume (reagent units) of non-evaporating reagents (blood, etc.) removed from an
    /// existing puddle per rain interval per tile.
    /// Tune alongside <see cref="PuddleInterval"/> for desired wash speed.
    /// Default 2 u per 30 s clears a typical blood puddle (~10 u) in ~5 passes / 2.5 minutes.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public float PuddleWashAmount = 2f;
}
