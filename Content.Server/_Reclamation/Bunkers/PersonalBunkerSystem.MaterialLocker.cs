using System.Linq;
using System.Numerics;
using Content.Server.Materials;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Reclamation.Bunkers;

public sealed partial class PersonalBunkerSystem
{
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;

    private void InitializeMaterialLocker()
    {
        SubscribeLocalEvent<BunkerMaterialLockerComponent, InteractUsingEvent>(OnLockerDeposit);
        SubscribeLocalEvent<BunkerMaterialLockerComponent, ContainerIsInsertingAttemptEvent>(OnDrawerInsert);
        SubscribeLocalEvent<BunkerMaterialLockerComponent, OpenBunkerSupplyDrawerMessage>(
            (uid, _, args) => _storage.OpenStorageUI(uid, args.Actor));
    }

    internal EntityUid EnsureMaterialLocker(EntityUid grid)
    {
        EntityUid? locker = null;
        var ladder = new Vector2(0.5f, -2.5f);
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<BunkerMaterialLockerComponent>(child))
                locker ??= child;
            else if (HasComp<PersonalBunkerExitComponent>(child))
                ladder = Transform(child).LocalPosition;
        }

        if (locker != null)
            return locker.Value;

        var mapGrid = Comp<MapGridComponent>(grid);
        var position = new EntityCoordinates(grid, ladder + Vector2.UnitX);
        // Existing rooms may already have furniture beside the ladder.
        foreach (var offset in new[] { Vector2.UnitX, -Vector2.UnitX, -Vector2.UnitY, Vector2.UnitY })
        {
            var candidate = new EntityCoordinates(grid, ladder + offset);
            var tile = _maps.GetTileRef((grid, mapGrid), candidate);
            if (tile.Tile.IsEmpty || _maps.GetAnchoredEntities(grid, mapGrid, tile.GridIndices).MoveNext())
                continue;
            position = candidate;
            break;
        }
        return Spawn("ReclamationBunkerMaterialLocker", position);
    }

    private bool CanPoolMaterial(EntityUid uid)
    {
        return HasComp<MaterialComponent>(uid) &&
            TryComp<PhysicalCompositionComponent>(uid, out var composition) &&
            composition.MaterialComposition.Count > 0 &&
            composition.MaterialComposition.All(p => p.Value > 0 && _materialStorage.GetSingleStackVolume(p.Key) > 0);
    }

    private void OnLockerDeposit(EntityUid uid, BunkerMaterialLockerComponent locker, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (EntityManager.IsQueuedForDeletion(args.Used))
            return;
        if (!IsBunkerCraftingMaterial(args.Used))
        {
            Notice(args.User, Loc.GetString("bunker-material-locker-supplies-only"));
            return;
        }

        var inserted = CanPoolMaterial(args.Used)
            ? _materialStorage.TryInsertMaterialEntity(args.User, args.Used, uid)
            : _storage.Insert(uid, args.Used, out _, args.User);

        if (!inserted)
            Notice(args.User, Loc.GetString("bunker-material-locker-full"));
    }

    private void OnDrawerInsert(EntityUid uid, BunkerMaterialLockerComponent locker, ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled || args.Container.ID != StorageComponent.ContainerId)
            return;

        // The lathe pool holds every material that can be ejected. Keep other building
        // supplies (e.g. cable coils) as their original items in the workbench drawer.
        var item = args.EntityUid;
        if (!IsBunkerCraftingMaterial(item) || CanPoolMaterial(item))
        {
            args.Cancel();
            return;
        }

        TryComp<StackComponent>(item, out var incoming);
        var max = _stacks.GetMaxCount(incoming);
        if (incoming != null && incoming.Count > (max == int.MaxValue ? 30 : max))
        {
            args.Cancel();
            return;
        }

        foreach (var stored in args.Container.ContainedEntities)
        {
            if (stored == item)
                continue;
            if (incoming != null ? TryComp<StackComponent>(stored, out var stack) && stack.StackTypeId == incoming.StackTypeId
                : Prototype(stored)?.ID == Prototype(item)?.ID)
            {
                args.Cancel();
                return;
            }
        }
    }
}
