// #Misfits Change/Add: Local client event raised when an entity drag ends without a valid entity drop target,
// allowing other systems (e.g., admin drag-teleport) to intercept and handle the drop coordinates.
using Robust.Shared.Map;

namespace Content.Client._Misfits.Administration.Events;

/// <summary>
/// Raised locally on the client when an entity drag-drop ends without finding
/// a valid entity target at the cursor position. Systems can set
/// <see cref="Handled"/> to suppress the default drag-cancel behavior and
/// receive a return value of <c>true</c> from the input handler.
/// </summary>
public sealed class DragNoTargetEvent : EntityEventArgs
{
    /// <summary>The entity that was being dragged.</summary>
    public readonly EntityUid DraggedEntity;

    /// <summary>The in-world coordinates where the mouse was released.</summary>
    public readonly EntityCoordinates TargetCoordinates;

    /// <summary>
    /// Set to <c>true</c> to indicate the event was handled.
    /// Prevents the "drag cancelled" popup and signals a successful drop.
    /// </summary>
    public bool Handled;

    public DragNoTargetEvent(EntityUid draggedEntity, EntityCoordinates targetCoordinates)
    {
        DraggedEntity = draggedEntity;
        TargetCoordinates = targetCoordinates;
    }
}
