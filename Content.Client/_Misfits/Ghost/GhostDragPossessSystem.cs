// #Misfits Change - Allows admin ghosts to drag regular ghosts onto entities to possess them
using Content.Shared.DragDrop;
using Content.Shared.Ghost;
using Content.Shared.Strip;
using Robust.Shared.GameObjects;

namespace Content.Client._Misfits.Ghost;

/// <summary>
/// Client-side system that enables admin ghosts to drag any ghost onto an entity
/// to make that ghost's player take control of the target ("control mob").
/// The user performing the drag must be an admin ghost (CanGhostInteract = true).
/// </summary>
public sealed class GhostDragPossessSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // Dragged-side events (on the ghost being dragged).
        SubscribeLocalEvent<GhostComponent, CanDragEvent>(OnCanDrag);
        SubscribeLocalEvent<GhostComponent, CanDropDraggedEvent>(OnCanDropDragged);

        // Target-side event (on the entity being dropped onto).
        // Uses TransformComponent so it applies to all entities.
        // Must run before SharedStrippableSystem which unconditionally sets Handled=true
        // on humanoids. Since stripping uses CanDrop |= (OR-assign), setting CanDrop=true
        // first ensures it stays true.
        SubscribeLocalEvent<TransformComponent, CanDropTargetEvent>(OnCanDropTarget,
            before: new[] { typeof(SharedStrippableSystem) });
    }

    /// <summary>
    /// Allow any ghost entity to be dragged. The interaction system already
    /// validates that the user (the one initiating the drag) can interact,
    /// which restricts this to admin ghosts.
    /// </summary>
    private void OnCanDrag(EntityUid uid, GhostComponent component, ref CanDragEvent args)
    {
        args.Handled = true;
    }

    private void OnCanDropDragged(EntityUid uid, GhostComponent component, ref CanDropDraggedEvent args)
    {
        // Only approve if the user doing the drag is an admin ghost.
        if (TryComp<GhostComponent>(args.User, out var userGhost) && userGhost.CanGhostInteract)
        {
            args.CanDrop = true;
            args.Handled = true;
        }
    }

    /// <summary>
    /// Approve any entity as a drop target when an admin ghost is dragging a ghost onto it.
    /// </summary>
    private void OnCanDropTarget(EntityUid uid, TransformComponent component, ref CanDropTargetEvent args)
    {
        // The dragged entity must be a ghost and the user must be an admin ghost.
        if (!HasComp<GhostComponent>(args.Dragged))
            return;

        if (!TryComp<GhostComponent>(args.User, out var userGhost) || !userGhost.CanGhostInteract)
            return;

        args.CanDrop = true;
        args.Handled = true;
    }
}
