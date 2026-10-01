// #Misfits Change - Server-side bleed amount change event for player pain reactions.
using Robust.Shared.GameObjects;

namespace Content.Server._Misfits.Chat.Events;

/// <summary>
/// Raised when an entity's bloodstream bleed amount changes.
/// </summary>
public sealed class BleedAmountChangedEvent(float previousBleedAmount, float newBleedAmount) : EntityEventArgs
{
    public float PreviousBleedAmount { get; } = previousBleedAmount;

    public float NewBleedAmount { get; } = newBleedAmount;
}