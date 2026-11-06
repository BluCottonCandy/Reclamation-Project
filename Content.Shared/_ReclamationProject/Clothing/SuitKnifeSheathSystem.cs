using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Item;
using Robust.Shared.Containers;

namespace Content.Shared._ReclamationProject.Clothing;

/// <summary>Uses existing item slots and clothing prefixes for a visible, removable chest knife.</summary>
public sealed class SuitKnifeSheathSystem : EntitySystem
{
    [Dependency] private readonly ClothingSystem _clothing = default!;
    [Dependency] private readonly SharedItemSystem _items = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SuitKnifeSheathComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SuitKnifeSheathComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SuitKnifeSheathComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnStartup(Entity<SuitKnifeSheathComponent> ent, ref ComponentStartup args) => UpdateVisuals(ent);

    private void OnInserted(Entity<SuitKnifeSheathComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.Slot)
            UpdateVisuals(ent);
    }

    private void OnRemoved(Entity<SuitKnifeSheathComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.Slot)
            UpdateVisuals(ent);
    }

    private void UpdateVisuals(Entity<SuitKnifeSheathComponent> ent)
    {
        var filled = _containers.TryGetContainer(ent, ent.Comp.Slot, out var container) && container.ContainedEntities.Count > 0;
        var prefix = filled ? "knife" : null;
        _clothing.SetEquippedPrefix(ent, prefix);
        _items.SetHeldPrefix(ent, prefix);
    }
}
