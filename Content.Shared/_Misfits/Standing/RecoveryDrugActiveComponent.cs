// Misfits Add: marker component for entities actively metabolizing a recovery drug (stimpak/healing powder)
namespace Content.Shared._Misfits.Standing;

/// <summary>
///     Marker added by recovery drug reagent effects (stimpaks, healing powder) while actively metabolizing.
///     When present on an entity exiting hard crit, causes TryStandUp to use the shorter
///     SoftCritStandingUpTime (2s) instead of the full CritStandingUpTime (8s).
///     Managed via GenericStatusEffect — expires a few seconds after the last metabolism tick.
/// </summary>
[RegisterComponent]
public sealed partial class RecoveryDrugActiveComponent : Component { }
