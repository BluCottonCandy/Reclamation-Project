// #Misfits Change — Commented out; only used by the Misfits CPR system which has been disabled.
/*
// #Misfits Add - CPR system component. Tracks cooldown state for entities performing CPR.
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Medical.CPR;

/// <summary>
/// Added temporarily to track CPR cooldown for a performer.
/// Removed after <see cref="CooldownDuration"/> seconds.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CPRCooldownComponent : Component
{
    /// <summary>How long (seconds) before CPR can be performed again.</summary>
    [DataField]
    public float CooldownDuration = 20f;

    public TimeSpan ExpireTime;
}
*/
