using System.Diagnostics;
using System.IO;
using System.Linq;
using Content.Server.Nutrition.Components;
using Content.Server.Paper;
using Content.Server.Storage.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Materials;
using Content.Shared.Nutrition.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._Reclamation.Bunkers;

public sealed partial class PersonalBunkerSystem
{
    private void QueueSave(Room room)
    {
        if (room.SaveQueued) return;
        room.SaveQueued = true;
        _saveRequests.Enqueue(room);
    }

    private bool CaptureQueuedSave()
    {
        while (_saveRequests.TryDequeue(out var room))
        {
            if (!room.SaveQueued) continue;
            room.SaveQueued = false;
            if (!_rooms.TryGetValue(room.Key, out var current) || current != room
                || !Exists(room.Grid) || Occupied(room)) continue;

            CaptureSnapshot(room);
            return true;
        }
        return false;
    }

    private bool CaptureSnapshot(Room room)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            _blueprints.PurgeTree(room.Grid);
            PrepareRoomContentsForSave(room.Grid, Occupied(room));

            EntityManager.CullRemovedComponents();
            _savingGrid = room.Grid;
            var (data, category) = _loader.SerializeEntitiesRecursive([room.Grid], new SerializationOptions
            {
                Category = FileCategory.Grid,
                MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
                ExpectPreInit = false,
            });
            if (category != FileCategory.Grid)
                throw new IOException("Bunker serialization did not produce a grid.");

            // Deep-copy the detached data so the worker cannot observe later mutations by
            // serialization subscribers/caches. All entity access remains on the game thread.
            _saves.Enqueue(room.Key, data.CopyCast<MappingDataNode>());
            Log.Info($"Bunker snapshot captured in {timer.Elapsed.TotalMilliseconds:F1} ms; queued for background publication.");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Bunker snapshot failed; prior save is preserved. No automatic retry; unsaved progress may be lost: {e}");
            return false;
        }
        finally
        {
            _savingGrid = null;
        }
    }

    internal void PrepareRoomContentsForSave(EntityUid grid, bool occupied)
    {
        var locker = EnsureMaterialLocker(grid);
        var drawer = Comp<StorageComponent>(locker).Container;
        // The traversal skips bodies and everything they carry, including nested bags.
        foreach (var uid in GetRoomBelongings(grid))
        {
            if (TerminatingOrDeleted(uid) || uid == locker || drawer.Contains(uid))
                continue;

            if (TryComp<MaterialStorageComponent>(uid, out var storage))
            {
                // Clear this machine's own pool, even if it is linked to a silo elsewhere.
                if (storage.Storage.Count > 0)
                {
                    storage.Storage.Clear();
                    Dirty(uid, storage);
                    var changed = new MaterialAmountChangedEvent();
                    RaiseLocalEvent(uid, ref changed);
                }
            }
            else if (IsBunkerCraftingMaterial(uid))
            {
                Del(uid);
                continue;
            }

            if (!occupied)
                _displays.Decommission(uid);
        }
    }

    private bool IsBunkerCraftingMaterial(EntityUid uid)
    {
        TryComp<StackComponent>(uid, out var stack);
        // Construction supplies that cannot be inserted into a material storage machine.
        if (stack?.StackTypeId.Id is "Cable" or "CableMV" or "CableHV" or "MetalRod" or
            "Cloth" or "Cotton" or "Pyrotton" or "Bones" or "MaterialGunpowder" or "GoliathHide" or "MeatSheets")
            return true;

        // Several non-supply items have Material for recycling, including caps, notes and lunchboxes.
        return HasComp<MaterialComponent>(uid) && !HasComp<CashComponent>(uid) &&
            !HasComp<PaperComponent>(uid) && !HasComp<FoodComponent>(uid) && !HasComp<DrinkComponent>(uid) &&
            !HasComp<StorageComponent>(uid) && !HasComp<EntityStorageComponent>(uid) &&
            stack?.StackTypeId.Id != "PrizeTicket";
    }

    private bool ReportSave(BunkerSaveQueue.Result? result)
    {
        if (result == null) return true;
        if (result.Error != null)
        {
            Log.Error($"Bunker publication failed; prior manifest is preserved. No automatic retry; unsaved progress may be lost: {result.Error}");
            return false;
        }
        Log.Info($"Published {result.Count} bunker snapshot(s) in {result.Duration.TotalMilliseconds:F1} ms on the background writer.");
        if (result.CleanupWarning != null)
            Log.Warning($"Bunker save succeeded, but old snapshot cleanup failed: {result.CleanupWarning}");
        return true;
    }

    /// <summary>
    /// Explicit safety saves drain older publications before capturing current rooms, then
    /// wait for the new publications. Normal exits never call this blocking path.
    /// </summary>
    public bool SaveAll(bool includeOccupied = false)
    {
        if (_rooms.Count == 0 && !_saves.HasWork) return true;
        var success = true;
        foreach (var result in _saves.Flush(Folder))
            success &= ReportSave(result);

        foreach (var room in _rooms.Values.Where(room => includeOccupied || !Occupied(room)).ToArray())
        {
            room.SaveQueued = false;
            success &= CaptureSnapshot(room);
        }
        foreach (var result in _saves.Flush(Folder))
            success &= ReportSave(result);
        return success;
    }
}
