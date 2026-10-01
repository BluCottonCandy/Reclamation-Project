using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Misfits.CCVar;

/// <summary>
/// CVars for the Misfits job-slot reclaim + same-round respawn lock system.
/// See <c>Content.Server/_Misfits/JobSlotReclaim/MisfitsJobSlotReclaimSystem.cs</c>.
/// </summary>
[CVarDefs]
public sealed class JobSlotReclaimCVars : CVars
{
    /// <summary>
    /// Seconds after a player dies in an occupied job slot before that slot is
    /// automatically re-opened for late-join. The per-character "died this
    /// round" lock is independent and is NOT cleared by this timer — only
    /// revival, cryo, or an admin <c>respawn</c> clears the lock.
    /// Default: 900 (15 minutes).
    /// </summary>
    public static readonly CVarDef<float> JobSlotReclaimSeconds =
        CVarDef.Create("misfits.job_slot_reclaim_seconds", 900f, CVar.SERVER | CVar.SERVERONLY);

    /// <summary>
    /// Whether the same-round respawn lock is enforced at all. Setting this
    /// false disables the lock check in the spawn flow; slot reclaim still
    /// works. Useful for debugging or admin events.
    /// </summary>
    public static readonly CVarDef<bool> JobSlotReclaimLockEnabled =
        CVarDef.Create("misfits.job_slot_reclaim_lock_enabled", true, CVar.SERVER | CVar.SERVERONLY);
}
