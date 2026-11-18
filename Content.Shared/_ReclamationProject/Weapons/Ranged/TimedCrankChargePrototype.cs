using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.Weapons.Ranged;

/// <summary>Settings for a crank gun identified by its exact entity prototype ID.</summary>
[Prototype("timedCrankCharge")]
public sealed partial class TimedCrankChargePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public TimeSpan CrankDelay;
    [DataField(required: true)] public float FireRateMultiplier;
    [DataField(required: true)] public TimeSpan HoldTime;
}
