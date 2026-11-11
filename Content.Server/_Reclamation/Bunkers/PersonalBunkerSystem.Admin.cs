using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Content.Shared._ReclamationProject.Bunkers;
using Content.Shared.Administration;
using Content.Shared.Atmos;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Reclamation.Bunkers;

public sealed partial class PersonalBunkerSystem
{
    private sealed record Inspection(EntityUid Map, EntityUid Grid);
    private readonly Dictionary<string, Inspection> _adminInspections = new();
    private readonly Dictionary<EntityUid, EntityCoordinates> _adminOrigins = new();
    private TimeSpan _nextAdminLoad;

    public static string BunkerKey(Guid user, int profile) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.ToString("N") + ":" + profile))).ToLowerInvariant();

    public async Task<List<AdminBunkerEntry>> GetAdminBunkerList()
    {
        var manifest = ReadManifest();
        var owners = await _db.GetBunkerOwnersAsync();
        var byKey = owners.ToDictionary(owner => BunkerKey(owner.UserId, owner.ProfileId));
        return manifest.Keys.Select(key => byKey.TryGetValue(key, out var owner)
                ? new AdminBunkerEntry(key, owner.CharacterName, owner.Ckey, _rooms.ContainsKey(key) || _adminInspections.ContainsKey(key))
                : new AdminBunkerEntry(key, "Unknown/deleted character (" + key[..Math.Min(8, key.Length)] + ")", "Unknown", _rooms.ContainsKey(key) || _adminInspections.ContainsKey(key)))
            .OrderBy(entry => entry.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Ckey, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Key).ToList();
    }

    public bool VisitAdminBunker(ICommonSession admin, string key, out string result)
    {
        result = "An active admin ghost is required to visit a bunker.";
        if (!_admins.HasAdminFlag(admin, AdminFlags.Admin) || admin.AttachedEntity is not { } ghost || !IsActiveAdminGhost(ghost))
            return false;
        if (key.Length != 64 || key.Any(c => !Uri.IsHexDigit(c)))
        {
            result = "Invalid bunker selection.";
            return false;
        }
        if (_time.CurTime < _nextAdminLoad)
        {
            result = "Please wait a moment before loading another bunker.";
            return false;
        }
        _nextAdminLoad = _time.CurTime + TimeSpan.FromSeconds(2);
        EntityUid? loadingMap = null;
        try
        {
            EntityUid grid;
            if (_rooms.TryGetValue(key, out var live) && Exists(live.Grid))
                grid = live.Grid;
            else if (_adminInspections.TryGetValue(key, out var inspection) && Exists(inspection.Grid))
                grid = inspection.Grid;
            else
            {
                // Resolve only through the current server manifest. Clients never supply a file path.
                var manifest = ReadManifest();
                if (!manifest.TryGetValue(key, out var filename))
                {
                    result = "This saved bunker is no longer available. Refresh the list.";
                    return false;
                }
                var path = SafeFile(filename);
                using var reader = new StringReader(BunkerSnapshotReferences.Clean(File.ReadAllText(path)));
                if (!_loader.TryLoadGrid(reader, filename, out var map, out var loadedGrid,
                        new DeserializationOptions { InitializeMaps = true }))
                    throw new IOException("Bunker grid could not be loaded.");
                loadingMap = map.Value.Owner;
                grid = loadedGrid.Value.Owner;
                var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
                air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
                _atmosphere.SetMapAtmosphere(loadingMap.Value, false, air);
                _gravity.EnableGravity(grid);
                // Inspection maps are deliberately absent from _rooms: they are never written over owner saves.
                _adminInspections[key] = new Inspection(loadingMap.Value, grid);
            }
            if (!_adminOrigins.ContainsKey(ghost)) _adminOrigins[ghost] = Transform(ghost).Coordinates;
            MoveWithDraggedItem(ghost, new EntityCoordinates(grid, new Vector2(.5f, -1.5f)));
            result = "Bunker loaded. You can use Return to go back. Unloaded saves open as inspection copies.";
            return true;
        }
        catch (Exception e)
        {
            if (loadingMap is { } map && Exists(map)) QueueDel(map);
            _adminInspections.Remove(key);
            Log.Error($"Admin bunker inspection failed for {key}: {e}");
            result = "The bunker could not be loaded. Its original save has not been changed.";
            return false;
        }
    }

    public bool ReturnAdminFromBunker(EntityUid ghost)
    {
        if (!IsActiveAdminGhost(ghost) || !_adminOrigins.TryGetValue(ghost, out var origin) || !origin.IsValid(EntityManager))
            return false;
        MoveWithDraggedItem(ghost, origin);
        _adminOrigins.Remove(ghost);
        return true;
    }

    private void UpdateAdminInspections()
    {
        foreach (var ghost in _adminOrigins.Keys.Where(uid => !Exists(uid)).ToArray()) _adminOrigins.Remove(ghost);
        foreach (var (key, inspection) in _adminInspections.ToArray())
        {
            // Never delete a map containing any connected player, even if another admin moved them here.
            if (HasOccupant<Robust.Shared.Player.ActorComponent>(inspection.Map)) continue;
            foreach (var ghost in _adminOrigins.Keys.Where(uid => Exists(uid) && Transform(uid).MapUid == inspection.Map).ToArray())
            {
                var origin = _adminOrigins[ghost];
                if (origin.IsValid(EntityManager)) MoveWithDraggedItem(ghost, origin);
                _adminOrigins.Remove(ghost);
            }
            _adminInspections.Remove(key);
            if (Exists(inspection.Map)) QueueDel(inspection.Map);
        }
    }
}
