using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.Atmos;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using Content.Server.Gravity;
using Content.Server.Warps;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Content.Shared.Mobs;
using Content.Shared.Ghost;
using Content.Shared.Bed.Sleep;

namespace Content.Server._Reclamation.Bunkers;

/// <summary>
/// Character-owned, server-side interiors. Reuses cryostorage, grid serialization and normal containers.
/// Character ownership uses the existing DB profile row, never a mutable name or current lobby selection.
/// </summary>
public sealed class PersonalBunkerSystem : EntitySystem
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly IGameTiming _time = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly WarperSystem _warper = default!;
    [Dependency] private readonly GravitySystem _gravity = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedRoofSystem _roofs = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly BunkerDisplayWeaponSystem _displays = default!;

    private sealed record Identity(string Account, Task<int?> Profile);
    private sealed class Room(string key, EntityUid map, EntityUid grid, EntityUid hatch)
    {
        public readonly string Key = key;
        public readonly EntityUid Map = map;
        public readonly EntityUid Grid = grid;
        public EntityUid Hatch = hatch;
        public TimeSpan? EmptySince;
    }
    private readonly Dictionary<EntityUid, Identity> _identities = new();
    private readonly Dictionary<string, Room> _rooms = new();
    private readonly Queue<(EntityUid Hatch, EntityUid User)> _loads = new();
    private readonly HashSet<EntityUid> _queued = new();
    private readonly HashSet<EntityUid> _cryoBypass = new();
    private readonly HashSet<EntityUid> _forcedExits = new();
    private EntityUid? _savingGrid;
    private TimeSpan _nextScan;
    private bool _stopping;
    private const int MaxRooms = 32;
    private const int MaxEntities = 1000;
    private const int MaxSaveBytes = 4 * 1024 * 1024;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeLocalEvent<PersonalBunkerEntranceComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<PersonalBunkerEntranceComponent, InteractHandEvent>(OnInteract);
        SubscribeLocalEvent<PersonalBunkerEntranceComponent, ContainerIsInsertingAttemptEvent>(OnInsert);
        SubscribeLocalEvent<PersonalBunkerEntranceComponent, PersonalBunkerChoiceMessage>(OnChoice);
        SubscribeLocalEvent<PersonalBunkerExitComponent, ActivateInWorldEvent>(OnExit);
        SubscribeLocalEvent<PersonalBunkerExitComponent, InteractHandEvent>(OnExitHand);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnd);
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(OnCleanup);
        _loader.OnIsSerializable += FilterSave;
    }

    public override void Shutdown()
    {
        if (!_stopping) SaveAll();
        _loader.OnIsSerializable -= FilterSave;
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        var prefs = _preferences.GetPreferences(args.Player.UserId);
        _identities[args.Mob] = new Identity(args.Player.UserId.UserId.ToString("N"),
            _db.GetBunkerProfileIdAsync(args.Player.UserId, prefs.SelectedCharacterIndex, args.Profile.Name));
    }

    private bool CanUse(EntityUid hatch, EntityUid user)
    {
        return !_stopping && Exists(hatch) && Exists(user) && HasComp<ActorComponent>(user)
            && HasComp<CanEnterCryostorageComponent>(user) && !_mobState.IsIncapacitated(user)
            && Transform(user).Coordinates.TryDistance(EntityManager, Transform(hatch).Coordinates, out var distance)
            && distance <= 2f;
    }

    private void Open(EntityUid hatch, EntityUid user)
    {
        if (CanUse(hatch, user))
            _ui.TryOpenUi(hatch, PersonalBunkerUiKey.Key, user);
    }
    private void OnActivate(Entity<PersonalBunkerEntranceComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled) return;
        Open(ent, args.User);
        args.Handled = true;
    }
    private void OnInteract(Entity<PersonalBunkerEntranceComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled) return;
        Open(ent, args.User);
        args.Handled = true;
    }
    private void OnInsert(Entity<PersonalBunkerEntranceComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (_cryoBypass.Contains(args.EntityUid)) return;
        args.Cancel();
        Open(ent, args.EntityUid);
    }
    private void OnChoice(Entity<PersonalBunkerEntranceComponent> ent, ref PersonalBunkerChoiceMessage args)
    {
        var user = args.Actor;
        if (!CanUse(ent, user)) return;
        _ui.CloseUi(ent.Owner, PersonalBunkerUiKey.Key, user);
        if (!args.Room)
        {
            if (!TryComp<CryostorageComponent>(ent, out var cryo)
                || !_containers.TryGetContainer(ent, cryo.ContainerId, out var container)) return;
            _cryoBypass.Add(user);
            try { _containers.Insert(user, container); }
            finally { _cryoBypass.Remove(user); }
            return;
        }
        if (_queued.Add(user)) _loads.Enqueue((ent, user));
    }

    private void Notice(EntityUid user, string text) => _popups.PopupEntity(text, user, user);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // At most one room is loaded per update, with duplicate requests coalesced.
        if (_loads.TryDequeue(out var request))
        {
            _queued.Remove(request.User);
            if (CanUse(request.Hatch, request.User)) Enter(request.Hatch, request.User);
        }
        if (_time.CurTime < _nextScan || _stopping) return;
        _nextScan = _time.CurTime + TimeSpan.FromSeconds(2);
        ProcessForcedExits();
        EvictUnattendedGhosts();
        foreach (var uid in _identities.Keys.Where(uid => !Exists(uid)).ToArray()) _identities.Remove(uid);
        foreach (var room in _rooms.Values)
        {
            if (Occupied(room)) room.EmptySince = null;
            else room.EmptySince ??= _time.CurTime;
        }
        var empty = _rooms.Values.Where(r => r.EmptySince is {} since && _time.CurTime - since >= TimeSpan.FromSeconds(10)).ToArray();
        if (empty.Length > 0)
        {
            if (!SaveAll()) return;
            foreach (var room in empty)
            {
                // Recheck after saving. Never delete an occupied room or its bodies.
                if (Occupied(room)) continue;
                _rooms.Remove(room.Key);
                QueueDel(room.Map);
            }
        }
    }

    private void Enter(EntityUid hatch, EntityUid user)
    {
        if (!_identities.TryGetValue(user, out var identity) || !identity.Profile.IsCompletedSuccessfully)
        {
            Notice(user, "Your character's room record is not ready. Please try again shortly.");
            return;
        }
#pragma warning disable RA0004 // IsCompletedSuccessfully above guarantees this read cannot block or deadlock.
        var completedProfile = identity.Profile.Result;
#pragma warning restore RA0004
        if (completedProfile is not {} profile)
        {
            Notice(user, "This character has no saved server profile yet.");
            return;
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Account + ":" + profile))).ToLowerInvariant();
        try
        {
            if (!_rooms.TryGetValue(key, out var room))
            {
                if (_rooms.Count >= MaxRooms) { Notice(user, "All bunker rooms are currently occupied. Please try later."); return; }
                var manifest = ReadManifest();
                EntityUid map;
                EntityUid grid;
                if (manifest.TryGetValue(key, out var filename))
                {
                    var path = SafeFile(filename);
                    if (!File.Exists(path) || new FileInfo(path).Length > MaxSaveBytes) throw new IOException("Invalid bunker save.");
                    using var reader = File.OpenText(path);
                    if (!_loader.TryLoadGrid(reader, filename, out var loadedMap, out var loadedGrid,
                        new DeserializationOptions { InitializeMaps = true })) throw new IOException("Bunker grid could not be loaded.");
                    map = loadedMap.Value.Owner;
                    grid = loadedGrid.Value.Owner;
                }
                else
                {
                    map = _maps.CreateMap(out var mapId);
                    var created = _maps.CreateGridEntity(mapId);
                    grid = created.Owner;
                    var roof = EnsureComp<RoofComponent>(grid);
                    for (var x = -4; x <= 4; x++)
                    for (var y = -4; y <= 4; y++)
                    {
                        var pos = new Vector2i(x, y);
                        _maps.SetTile(created, pos, new Tile(_tiles["N14FloorConcrete"].TileId));
                        _roofs.SetRoof((grid, created.Comp, roof), pos, true);
                        if (Math.Abs(x) == 4 || Math.Abs(y) == 4)
                            Spawn("ReclamationPersonalBunkerWall", new EntityCoordinates(grid, new Vector2(x + .5f, y + .5f)));
                    }
                    Spawn("ReclamationPersonalBunkerExit", new EntityCoordinates(grid, new Vector2(.5f, -2.5f)));
                }
                // Saved runtime entities may already be map-initialized. Enforce display rules after every load too.
                var displays = EntityQueryEnumerator<BunkerDisplayWeaponComponent, TransformComponent>();
                while (displays.MoveNext(out var display, out _, out var transform))
                    if (transform.GridUid == grid) _displays.Decommission(display);
                var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
                air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
                _atmosphere.SetMapAtmosphere(map, false, air);
                _gravity.EnableGravity(grid);
                room = new Room(key, map, grid, hatch);
                _rooms.Add(key, room);
            }
            room.Hatch = hatch;
            room.EmptySince = null;
            MoveWithDraggedItem(user, new EntityCoordinates(room.Grid, new Vector2(.5f, -1.5f)));
        }
        catch (Exception e)
        {
            Log.Error($"Could not open bunker {key}: {e}");
            Notice(user, "Your room could not be loaded. Your previous save has been preserved.");
        }
    }

    private void OnExit(Entity<PersonalBunkerExitComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled) return;
        Exit(ent, args.User);
        args.Handled = true;
    }
    private void OnExitHand(Entity<PersonalBunkerExitComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled) return;
        Exit(ent, args.User);
        args.Handled = true;
    }
    private void Exit(EntityUid ladder, EntityUid user)
    {
        if (!Transform(user).Coordinates.TryDistance(EntityManager, Transform(ladder).Coordinates, out var distance) || distance > 2) return;
        var grid = Transform(ladder).GridUid;
        var room = _rooms.Values.FirstOrDefault(r => r.Grid == grid);
        if (room == null || Transform(user).GridUid != grid) return;
        ReturnToHatch(room, user);
    }

    private bool ReturnToHatch(Room room, EntityUid user)
    {
        var hatch = room.Hatch;
        if (!Exists(hatch))
        {
            var query = EntityQueryEnumerator<PersonalBunkerEntranceComponent>();
            if (!query.MoveNext(out hatch, out _)) { Notice(user, "No safe return hatch exists. Ask an administrator for help."); return false; }
        }
        MoveWithDraggedItem(user, Transform(hatch).Coordinates);
        if (Transform(user).MapUid == room.Map) return false;
        if (!Occupied(room)) SaveAll();
        return true;
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState is MobState.Critical or MobState.SoftCritical or MobState.Dead)
            QueueForcedExit(args.Target);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args) => QueueForcedExit(args.Entity);

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is SessionStatus.Disconnected or SessionStatus.Zombie
            && args.Session.AttachedEntity is {} body)
            QueueForcedExit(body);
    }

    private void QueueForcedExit(EntityUid body)
    {
        if (Exists(body) && _rooms.Values.Any(room => room.Map == Transform(body).MapUid))
            _forcedExits.Add(body);
    }

    private void ProcessForcedExits()
    {
        // Moving bodies from here avoids changing maps during mob-state or session event dispatch.
        foreach (var body in _forcedExits.ToArray())
        {
            _forcedExits.Remove(body);
            if (!Exists(body)) continue;
            if (_mobState.IsAlive(body) && TryComp<ActorComponent>(body, out var actor)
                && actor.PlayerSession.Status == SessionStatus.InGame) continue;
            var room = _rooms.Values.FirstOrDefault(room => room.Map == Transform(body).MapUid);
            if (room != null && !ReturnToHatch(room, body)) _forcedExits.Add(body);
        }
    }

    private bool HasAwakePlayer(Room room)
    {
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var transform))
            if (transform.MapUid == room.Map && actor.PlayerSession.Status == SessionStatus.InGame
                && !HasComp<GhostComponent>(uid) && _mobState.IsAlive(uid) && !HasComp<SleepingComponent>(uid))
                return true;
        return false;
    }

    private void EvictUnattendedGhosts()
    {
        foreach (var room in _rooms.Values.ToArray())
        {
            if (HasAwakePlayer(room)) continue;
            var ghosts = new List<EntityUid>();
            var query = EntityQueryEnumerator<GhostComponent, TransformComponent>();
            while (query.MoveNext(out var ghost, out _, out var transform))
                if (transform.MapUid == room.Map) ghosts.Add(ghost);
            foreach (var ghost in ghosts) ReturnToHatch(room, ghost);
        }
    }

    private void MoveWithDraggedItem(EntityUid user, EntityCoordinates destination)
    {
        // Reuse normal teleport handling for pulling, velocity and followers.
        var marker = Spawn(null, destination);
        try
        {
            if (!_warper.WarpEntityTo(user, marker))
                Notice(user, "The bunker destination is unavailable. Please try again.");
        }
        finally { QueueDel(marker); }
    }

    private bool Occupied(Room room)
    {
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var transform))
            if (transform.MapUid == room.Map && IsLivingEntity(uid)) return true;
        return false;
    }
    private bool IsLivingEntity(EntityUid uid) => HasComp<GhostComponent>(uid) || HasComp<MobStateComponent>(uid) || HasComp<MindContainerComponent>(uid) || HasComp<ActorComponent>(uid);

    /// <summary>Also excludes every child of a mob, including worn equipment and held bags.</summary>
    public bool IsRoomBelonging(EntityUid uid, EntityUid grid)
    {
        if (uid == grid) return true;
        var current = uid;
        for (var depth = 0; depth < 128; depth++)
        {
            if (IsLivingEntity(current)) return false;
            var parent = Transform(current).ParentUid;
            if (parent == grid) return true;
            if (!parent.IsValid() || !Exists(parent)) return false;
            current = parent;
        }
        return false;
    }
    private void FilterSave(Entity<MetaDataComponent> ent, ref bool serializable)
    {
        if (_savingGrid is {} grid && !IsRoomBelonging(ent, grid)) serializable = false;
    }

    private string Folder => Path.Combine(_resources.UserData.RootDir ?? throw new IOException("Server storage unavailable."), "reclamation_bunkers");
    private string SafeFile(string filename)
    {
        if (filename.Length != 101 || filename[64] != '_' || !filename.EndsWith(".yml", StringComparison.Ordinal)
            || filename.Take(64).Concat(filename.Skip(65).Take(32)).Any(c => !Uri.IsHexDigit(c)))
            throw new IOException("Invalid bunker snapshot filename.");
        return Path.Combine(Folder, filename);
    }
    private Dictionary<string, string> ReadManifest()
    {
        var path = Path.Combine(Folder, "manifest.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new IOException("Invalid bunker manifest.") : new();
    }

    /// <summary>Publish all active rooms in one manifest transaction, avoiding cross-room snapshot duplication.</summary>
    public bool SaveAll(bool includeOccupied = false)
    {
        var roomsToSave = _rooms.Values.Where(room => includeOccupied || !Occupied(room)).ToArray();
        if (roomsToSave.Length == 0) return true;
        try
        {
            Directory.CreateDirectory(Folder);
            var manifest = ReadManifest();
            foreach (var room in roomsToSave)
            {
                var belongings = new List<EntityUid>();
                var query = EntityQueryEnumerator<TransformComponent>();
                while (query.MoveNext(out var uid, out _))
                    if (IsRoomBelonging(uid, room.Grid)) belongings.Add(uid);
                if (belongings.Count > MaxEntities) throw new IOException("Bunker entity limit exceeded.");
                // Autosaving an occupied room must never change weapons being used inside it.
                // Only abandoned room belongings become decorations after the last occupant leaves.
                if (!Occupied(room))
                    foreach (var uid in belongings) _displays.Decommission(uid);
                EntityManager.CullRemovedComponents();
                _savingGrid = room.Grid;
                using var writer = new StringWriter();
                if (!_loader.TrySaveGrid(room.Grid, writer, new SerializationOptions
                    { MissingEntityBehaviour = MissingEntityBehaviour.Ignore, ExpectPreInit = false }))
                    throw new IOException("Bunker serialization failed.");
                _savingGrid = null;
                var yaml = writer.ToString();
                if (Encoding.UTF8.GetByteCount(yaml) > MaxSaveBytes) throw new IOException("Bunker save size limit exceeded.");
                var filename = room.Key + "_" + Guid.NewGuid().ToString("N") + ".yml";
                var path = SafeFile(filename);
                File.WriteAllText(path, yaml, Encoding.UTF8);
                manifest[room.Key] = filename;
            }
            var target = Path.Combine(Folder, "manifest.json");
            var temporary = Path.Combine(Folder, "manifest.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest), Encoding.UTF8);
            if (File.Exists(target)) File.Copy(target, Path.Combine(Folder, "manifest.previous.json"), true);
            File.Move(temporary, target, true);
            // Keep the current and previous generation; remove only our own validated snapshots.
            var retained = manifest.Values.ToHashSet();
            var backup = Path.Combine(Folder, "manifest.previous.json");
            if (File.Exists(backup)) retained.UnionWith(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(backup))!.Values);
            foreach (var file in Directory.EnumerateFiles(Folder, "*.yml"))
            {
                var name = Path.GetFileName(file);
                if (!retained.Contains(name) && SafeFile(name) == file) File.Delete(file);
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Bunker save failed; rooms remain loaded and prior manifest is preserved: {e}");
            return false;
        }
        finally { _savingGrid = null; }
    }
    public void SaveBeforeShutdown()
    {
        SaveAll(includeOccupied: true);
        _stopping = true;
    }

    private void OnRoundEnd(RoundEndedEvent args) => SaveAll(includeOccupied: true);
    private void OnCleanup(Content.Shared.GameTicking.RoundRestartCleanupEvent args)
    {
        SaveAll(includeOccupied: true);
        _rooms.Clear();
        _identities.Clear();
        _loads.Clear();
        _queued.Clear();
        _forcedExits.Clear();
        _stopping = false;
    }
}
