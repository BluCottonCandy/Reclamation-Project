// #Misfits Change Add - Tracks a double-grab escalation on the aggressor entity.
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Misfits.Grabbing.Components;

public enum DoubleGrabPhase : byte
{
    /// <summary>Wind-up: aggressor holds a second grab for <see cref="DoubleGrabComponent.PinTime"/>.</summary>
    Pending,
    /// <summary>Active: victim is being carried and choked.</summary>
    Active,
}

/// <summary>
/// Added to the aggressor when a second grab attempt is made on a target already being pulled.
/// Transitions from <see cref="DoubleGrabPhase.Pending"/> to <see cref="DoubleGrabPhase.Active"/>
/// when the wind-up timer completes.
/// </summary>
[RegisterComponent]
public sealed partial class DoubleGrabComponent : Component
{
    [DataField]
    public EntityUid Victim;

    [DataField]
    public DoubleGrabPhase Phase = DoubleGrabPhase.Pending;

    /// <summary>How long the current phase has been held.</summary>
    [DataField]
    public TimeSpan HeldTime = TimeSpan.Zero;

    // ── Pending-phase settings ──────────────────────────────────────────────

    /// <summary>Duration the second grab must be maintained before the carry is forced.</summary>
    [DataField]
    public TimeSpan PinTime = TimeSpan.FromSeconds(10);

    // ── Active-phase settings ───────────────────────────────────────────────

    /// <summary>Time after the carry begins before oxygen drain starts.</summary>
    [DataField]
    public TimeSpan SuffocationStartTime = TimeSpan.FromSeconds(15);

    /// <summary>Time after the carry begins at which the victim is forced into critical state.</summary>
    [DataField]
    public TimeSpan CritTime = TimeSpan.FromSeconds(30);

    [DataField]
    public float CarrySpeedModifier = 0.3f;

    [DataField]
    public float SuffocationDrainPerSecond = 0.5f;

    [DataField]
    public bool CritApplied;
}
