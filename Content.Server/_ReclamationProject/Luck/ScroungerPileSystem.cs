using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Robust.Shared.Containers;

namespace Content.Server._ReclamationProject.Luck;

/// <summary>A finite find, opened once rather than used as a storage window.</summary>
[RegisterComponent]
public sealed partial class ScroungerPileComponent : Component
{
    public bool Opened;
}

public sealed class ScroungerPileSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ScroungerPileComponent, InteractHandEvent>(OnInteract);
        SubscribeLocalEvent<ScroungerPileComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnInteract(Entity<ScroungerPileComponent> ent, ref InteractHandEvent args)
    {
        if (!args.Handled)
            args.Handled = TryOpen(ent, args.User);
    }

    private void OnActivate(Entity<ScroungerPileComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!args.Handled)
            args.Handled = TryOpen(ent, args.User);
    }

    private bool TryOpen(Entity<ScroungerPileComponent> ent, EntityUid user)
    {
        if (ent.Comp.Opened || !_blocker.CanInteract(user, ent) ||
            !_interaction.InRangeUnobstructed(user, ent.Owner) ||
            !_containers.TryGetContainer(ent, "storagebase", out var contents))
            return false;
        ent.Comp.Opened = true;
        // Reuse the same container emptying used when ordinary storage is destroyed.
        // Move the actual loot before deleting its parent, so no reroll or duplicate is possible.
        _containers.EmptyContainer(contents, destination: _transform.GetMoverCoordinates(ent));
        if (contents.ContainedEntities.Count != 0)
        {
            ent.Comp.Opened = false;
            return false;
        }
        QueueDel(ent);
        return true;
    }
}
