using System.Diagnostics;
using System.IO;
using System.Linq;
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
            // A final safety save may include occupied rooms. Never decommission equipment
            // being used inside them; retain the same player/body exclusion as normal saves.
            if (!Occupied(room))
                foreach (var uid in GetRoomBelongings(room.Grid))
                    if (Exists(uid)) _displays.Decommission(uid);

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
