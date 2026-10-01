// #Misfits Change/Add: Bypass interaction range checks for admin ghosts (aghost).
// Admin ghosts (CanGhostInteract = true) should be able to interact with, drag, and drop
// entities from any distance — consistent with their full-admin access level and NoClip movement.
// Without this, drag-drop features (AdminDragTeleportSystem, GhostDragPossessSystem) were
// blocked by the base DragDropSystem and SharedDragDropSystem range checks, making them
// unusable unless the admin ghost happened to be within ~1.5 tiles of both entities at once.
using Content.Shared.Ghost;
using Content.Shared.Interaction;

namespace Content.Shared._Misfits.Ghost;

/// <summary>
/// Bypasses <see cref="InRangeOverrideEvent"/> range checks for admin ghosts,
/// i.e. any entity with <see cref="GhostComponent.CanGhostInteract"/> set to <c>true</c>
/// (the flag applied by the <c>aghost</c> command).
/// <para>
/// This means aghost admins can initiate drags, drop entities, and interact with anything
/// on the entire map without needing to be physically adjacent — matching the intent of the
/// AdminDragTeleportSystem and GhostDragPossessSystem Misfits features.
/// </para>
/// </summary>
public sealed class SharedAGhostRangeOverrideSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // Raised directed on the origin entity during InRangeUnobstructed checks.
        // Handling it on GhostComponent allows us to intercept all range checks where
        // the admin ghost is the initiator (user/dragger).
        SubscribeLocalEvent<GhostComponent, InRangeOverrideEvent>(OnInRangeOverride);
    }

    private void OnInRangeOverride(Entity<GhostComponent> ent, ref InRangeOverrideEvent args)
    {
        // Only bypass range for admin ghosts — regular wandering ghosts are NOT affected.
        if (!ent.Comp.CanGhostInteract)
            return;

        // Signal that this entity is always "in range" of its target.
        args.InRange = true;
        args.Handled = true;
    }
}
