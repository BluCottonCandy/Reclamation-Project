using Content.Shared.Inventory;

namespace Content.Shared.Atmos;

/// <summary>
/// Raised on a burning entity to check its fire protection.
/// Damage taken is multiplied by the final amount, but not temperature.
/// TemperatureProtection is needed for that.
/// </summary>
[ByRefEvent]
public sealed class GetFireProtectionEvent : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots { get; } = ~SlotFlags.POCKET;

    /// <summary>
    /// What to multiply the fire damage by.
    /// If this is 0 then it's ignored
    /// </summary>
    public float Multiplier;
    private float _minimumMultiplier;

    public GetFireProtectionEvent()
    {
        Multiplier = 1f;
    }

    /// <summary>
    /// Reduce fire damage taken by a percentage.
    /// </summary>
    public void Reduce(float by, float maximumReduction = 1f)
    {
        _minimumMultiplier = Math.Max(_minimumMultiplier, Math.Clamp(1f - maximumReduction, 0f, 1f));
        // Stacked protection must never turn burning into healing.
        Multiplier = Math.Clamp(Multiplier - by, _minimumMultiplier, 1f);
    }
}
