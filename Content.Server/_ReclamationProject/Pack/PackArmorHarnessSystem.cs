using Content.Shared._ReclamationProject.Pack;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.Containers;

namespace Content.Server._ReclamationProject.Pack;

public sealed class PackArmorHarnessSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PackArmorHarnessComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<PackArmorHarnessComponent, MapInitEvent>(OnMapInit);
    }

    private void OnUnequipped(EntityUid uid, PackArmorHarnessComponent component, GotUnequippedEvent args)
    {
        // Direct container transfers and destroyed armor can bypass InventorySystem.TryUnequip.
        if (args.Slot != "outerClothing" || TerminatingOrDeleted(args.Equipee))
            return;

        _inventory.TryUnequip(args.Equipee, PackArmorHarnessComponent.BowSlot, silent: true, force: true);
        _inventory.TryUnequip(args.Equipee, PackArmorHarnessComponent.QuiverSlot, silent: true, force: true);
    }

    private void OnMapInit(EntityUid uid, PackArmorHarnessComponent component, MapInitEvent args)
    {
        // A saved vest can still contain a bow from the old context-menu sling.
        if (_containers.TryGetContainer(uid, "bow_sling", out var oldSling) &&
            oldSling is ContainerSlot { ContainedEntity: {} bow })
            _transform.DropNextTo(bow, uid);
    }
}
