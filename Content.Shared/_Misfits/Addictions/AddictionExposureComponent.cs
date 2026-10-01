// #Misfits Change /Add:/ Tracks per-drug exposure counts before a full addiction is applied.
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Addictions;

/// <summary>
///     Tracks how many times each addictive reagent has affected an entity before they cross
///     the threshold into a full addiction.
/// </summary>
[RegisterComponent, Access(typeof(SharedAddictionSystem))]
public sealed partial class AddictionExposureComponent : Component
{
    /// <summary>
    ///     Number of times each addictive reagent has been metabolized on this entity.
    /// </summary>
    public Dictionary<ProtoId<ReagentPrototype>, int> ExposureCounts = new();

    /// <summary>
    ///     Last time this reagent was seen during metabolism.
    ///     Used to collapse a continuous bloodstream presence into a single exposure.
    /// </summary>
    public Dictionary<ProtoId<ReagentPrototype>, TimeSpan> LastSeenTimes = new();

    /// <summary>
    ///     Last observed quantity for the reagent in the metabolized solution.
    ///     A quantity increase indicates the player dosed again before the prior amount fully cleared.
    /// </summary>
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> LastSeenQuantities = new();
}