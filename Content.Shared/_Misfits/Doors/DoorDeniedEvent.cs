// #Misfits Change/Add: Directed event raised on a door entity when access is denied.
// Carries the attempting user so server-side systems can send feedback (e.g., popup).

namespace Content.Shared._Misfits.Doors;

/// <summary>
/// Raised as a directed event on a door entity after it enters the Denying state.
/// <see cref="User"/> is the entity that attempted to open the door, if known.
/// </summary>
public sealed class DoorDeniedEvent : EntityEventArgs
{
    /// <summary>
    /// The entity that tried (and failed) to open the door. May be null for automated/predicted events.
    /// </summary>
    public readonly EntityUid? User;

    public DoorDeniedEvent(EntityUid? user)
    {
        User = user;
    }
}
