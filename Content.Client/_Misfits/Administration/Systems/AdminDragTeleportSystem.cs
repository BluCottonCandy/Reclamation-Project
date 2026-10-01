// #Misfits Change/Add: Client-side system that intercepts drag-drop ending on empty space for admins,
// teleporting the dragged entity to the cursor release position.
// #Misfits Tweak: Restricted to aghost mode only (admin must be a ghost with CanGhostInteract).
using Content.Client._Misfits.Administration.Events;
using Content.Client.Administration.Managers;
using Content.Shared._Misfits.Administration;
using Content.Shared.DragDrop;
using Content.Shared.Ghost;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client._Misfits.Administration.Systems;

/// <summary>
/// Allows admins in aghost mode to initiate a drag on ANY entity (via <see cref="CanDragEvent"/>) and
/// teleport it to wherever they release the cursor when no valid entity drop target is found.
/// <para>
/// Two responsibilities:
/// 1. Subscribe <see cref="CanDragEvent"/> on <see cref="SpriteComponent"/> (present on all
///    visible entities) so aghost admins can start a drag on entities that normally don't support it.
/// 2. Subscribe <see cref="DragNoTargetEvent"/> to send <see cref="AdminSelfDragTeleportEvent"/>
///    to the server when the drag ends on empty space.
/// </para>
/// </summary>
public sealed class AdminDragTeleportSystem : EntitySystem
{
    [Dependency] private readonly IClientAdminManager _adminManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Allow aghost admins to drag any entity that has a sprite (i.e. every visible entity).
        // CanDragEvent has no user context, so we check local admin + ghost state as the gate.
        SubscribeLocalEvent<SpriteComponent, CanDragEvent>(OnCanDrag);

        // Intercept drag-drops that ended without a valid entity target.
        SubscribeLocalEvent<DragNoTargetEvent>(OnDragNoTarget);
    }

    /// <summary>
    /// Returns true only when the local player is an admin currently in aghost mode
    /// (i.e. is a ghost entity with CanGhostInteract set by the aghost command).
    /// </summary>
    private bool IsAdminGhost()
    {
        if (!_adminManager.IsAdmin())
            return false;

        var localEntity = _playerManager.LocalEntity;
        if (localEntity == null)
            return false;

        // CanGhostInteract is the flag set on admin ghosts by the aghost command.
        return TryComp<GhostComponent>(localEntity.Value, out var ghost) && ghost.CanGhostInteract;
    }

    /// <summary>
    /// Marks any sprite-bearing entity as draggable when the local player is in aghost mode.
    /// This is purely client-side; no gameplay change occurs until the drag is released.
    /// </summary>
    private void OnCanDrag(EntityUid uid, SpriteComponent component, ref CanDragEvent args)
    {
        // Only aghost admins may initiate the drag-teleport.
        if (!IsAdminGhost())
            return;

        args.Handled = true;
    }

    private void OnDragNoTarget(DragNoTargetEvent ev)
    {
        // Require aghost mode on the client (server will re-validate before acting).
        if (!IsAdminGhost())
            return;

        // Send the teleport request to the server with both the entity and target coordinates.
        RaiseNetworkEvent(new AdminSelfDragTeleportEvent(
            GetNetEntity(ev.DraggedEntity),
            GetNetCoordinates(ev.TargetCoordinates)));

        // Mark as handled so DragDropSystem signals a successful drop instead of a cancel.
        ev.Handled = true;
    }
}
