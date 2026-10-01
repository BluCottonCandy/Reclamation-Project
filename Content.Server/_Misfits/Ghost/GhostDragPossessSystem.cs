// #Misfits Change - Allows admin ghosts to drag regular ghosts onto entities to possess them
using Content.Server.Mind;
using Content.Shared.DragDrop;
using Content.Shared.Ghost;

namespace Content.Server._Misfits.Ghost;

/// <summary>
/// Server-side system that handles admin ghost drag-and-drop possession.
/// When an admin ghost (the user) drags any ghost onto a target entity,
/// the dragged ghost's player takes control of that target,
/// equivalent to the "control mob" admin verb.
/// </summary>
public sealed class GhostDragPossessSystem : EntitySystem
{
    [Dependency] private readonly MindSystem _mindSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GhostComponent, DragDropDraggedEvent>(OnGhostDragDropped);
    }

    private void OnGhostDragDropped(EntityUid uid, GhostComponent component, ref DragDropDraggedEvent args)
    {
        if (args.Handled)
            return;

        // The user performing the drag must be an admin ghost.
        if (!TryComp<GhostComponent>(args.User, out var userGhost) || !userGhost.CanGhostInteract)
            return;

        // Transfer the dragged ghost's mind into the target entity.
        _mindSystem.ControlMob(uid, args.Target);
        args.Handled = true;
    }
}
