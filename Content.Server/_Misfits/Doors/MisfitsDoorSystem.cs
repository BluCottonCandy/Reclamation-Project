// #Misfits Change/Add: Server-side handler for door access denial feedback.
// Routes the "door denied" message to the player's chatbox instead of a sprite popup,
// so it is never missed when the player is not watching the sprite closely.

using Content.Server.Chat.Managers;
using Content.Shared._Misfits.Doors;
using Content.Shared.Chat;
using Content.Shared.Doors.Components;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Doors;

/// <summary>
/// Handles player feedback when a door denies them access.
/// Listens for <see cref="DoorDeniedEvent"/> and sends a private chat message to the denied player.
/// </summary>
public sealed class MisfitsDoorSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Subscribe to the door denied event raised by SharedDoorSystem.Deny().
        SubscribeLocalEvent<DoorComponent, DoorDeniedEvent>(OnDoorDenied);
    }

    /// <summary>
    /// Sends a private chat message to the player informing them the door will not open for them.
    /// Uses IChatManager.ChatMessageToOne so only the denied player sees it in their chatbox.
    /// </summary>
    private void OnDoorDenied(EntityUid uid, DoorComponent comp, DoorDeniedEvent args)
    {
        // No user to notify (e.g. automated trigger) — nothing to do.
        if (args.User == null)
            return;

        // Resolve the player session — entity may not have one (NPC, etc.).
        if (!_playerManager.TryGetSessionByEntity(args.User.Value, out var session)
            || session.Status != SessionStatus.InGame)
            return;

        var message = Loc.GetString("door-access-denied-popup");

        // Send directly to one player's chat so it appears in the chatbox, not as a sprite popup.
        _chatManager.ChatMessageToOne(
            ChatChannel.Local,
            message,
            message,
            EntityUid.Invalid,
            false,
            session.Channel);
    }
}
