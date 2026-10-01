// #Misfits Add - Event raised on the blocker when they deflect a thrown spear.
// Handled server-side by SpearBlockChatSystem to emit a bystander emote.
namespace Content.Shared._Misfits.Throwing;

/// <summary>
/// Raised on the entity that deflected a spear (<see cref="Blocker"/>).
/// Contains enough context for the server-side chat handler to build the emote string.
/// </summary>
[ByRefEvent]
public readonly record struct SpearBlockedEvent(
    EntityUid Blocker,
    EntityUid Thrown,
    EntityUid? Thrower,
    EntityUid? BlockEntity
);
