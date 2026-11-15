using Content.Server.EUI;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.Ghost;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using System.Numerics;
using System.Linq;

namespace Content.Server._Reclamation.Bunkers;

public sealed partial class PersonalBunkerSystem
{
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly SharedTransformSystem _visitorTransform = default!;
    [Dependency] private readonly SharedPhysicsSystem _visitorPhysics = default!;

    private sealed record VisitRequest(Room Room, EntityUid Hatch, EntityUid Visitor, EntityUid Owner,
        TimeSpan Expires, BunkerVisitPromptEui Prompt);

    private readonly Dictionary<EntityUid, VisitRequest> _visitRequests = new();
    private readonly Dictionary<EntityUid, (EntityUid Map, EntityUid Hatch)> _visitorHatches = new();

    private bool IsVisitPlayer(EntityUid uid) =>
        TryComp<ActorComponent>(uid, out var actor) && actor.PlayerSession.Status == SessionStatus.InGame
        && actor.PlayerSession.AttachedEntity == uid && !HasComp<GhostComponent>(uid)
        && !_mobState.IsIncapacitated(uid);

    private bool CanRequestVisit(EntityUid hatch, EntityUid visitor) =>
        CanUse(hatch, visitor) && IsVisitPlayer(visitor) && !_containers.IsEntityInContainer(visitor)
        && _ui.IsUiOpen(hatch, PersonalBunkerUiKey.Key, visitor);

    private void OnListVisits(Entity<PersonalBunkerEntranceComponent> ent, ref ListBunkerVisitorsMessage args)
    {
        var visitor = args.Actor;
        if (!CanRequestVisit(ent, visitor))
            return;

        var entries = _rooms.Values
            .Where(room => room.Owner != visitor && HasActivePlayer(room, requireAwake: false))
            .OrderBy(room => room.OwnerName, StringComparer.OrdinalIgnoreCase)
            .Select(room => new BunkerVisitorEntry(GetNetEntity(room.Map), room.OwnerName,
                IsVisitPlayer(room.Owner)))
            .ToArray();
        _ui.ServerSendUiMessage(ent.Owner, PersonalBunkerUiKey.Key, new BunkerVisitorListMessage(entries), visitor);
    }

    private void OnRequestVisit(Entity<PersonalBunkerEntranceComponent> ent, ref RequestBunkerVisitMessage args)
    {
        var visitor = args.Actor;
        if (!CanRequestVisit(ent, visitor))
            return;

        var map = args.Map;
        var room = _rooms.Values.FirstOrDefault(room => GetNetEntity(room.Map) == map);
        if (room == null || room.Owner == visitor || !HasActivePlayer(room, requireAwake: false)
            || !IsVisitPlayer(room.Owner) || !TryComp<ActorComponent>(room.Owner, out var owner))
        {
            Notice(visitor, Loc.GetString("bunker-visit-unavailable"));
            return;
        }

        if (_visitRequests.ContainsKey(visitor) || _visitRequests.Values.Any(request => request.Owner == room.Owner)
            || room.NextVisitRequest > _time.CurTime)
        {
            Notice(visitor, Loc.GetString("bunker-visit-pending"));
            return;
        }

        var prompt = new BunkerVisitPromptEui(Name(visitor), approved => ResolveVisit(visitor, approved));
        _visitRequests.Add(visitor, new VisitRequest(room, ent, visitor, room.Owner,
            _time.CurTime + TimeSpan.FromSeconds(30), prompt));
        room.NextVisitRequest = _time.CurTime + TimeSpan.FromSeconds(10);
        _eui.OpenEui(prompt, owner.PlayerSession);
        Notice(visitor, Loc.GetString("bunker-visit-sent"));
    }

    private bool CanResolveVisit(VisitRequest request) =>
        _time.CurTime < request.Expires && CanRequestVisit(request.Hatch, request.Visitor)
        && _rooms.TryGetValue(request.Room.Key, out var room) && room == request.Room
        && room.Owner == request.Owner && IsVisitPlayer(request.Owner)
        && Exists(room.Grid) && HasActivePlayer(room, requireAwake: false);

    private void ResolveVisit(EntityUid visitor, bool approved)
    {
        if (!_visitRequests.Remove(visitor, out var request))
            return;

        if (!CanResolveVisit(request))
        {
            if (IsVisitPlayer(visitor))
                Notice(visitor, Loc.GetString("bunker-visit-unavailable"));
            return;
        }

        if (!approved)
        {
            Notice(visitor, Loc.GetString("bunker-visit-denied"));
            return;
        }

        var room = request.Room;
        var map = Transform(room.Grid).MapID;
        if (!_maps.MapExists(map) || !_maps.IsInitialized(map) || _maps.IsPaused(map))
        {
            Notice(visitor, Loc.GetString("bunker-visit-unavailable"));
            return;
        }

        // An invitation is for the requester alone, not pulled entities or followers.
        _visitorTransform.SetCoordinates(visitor, new EntityCoordinates(room.Grid, BunkerEntryOffset));
        if (HasComp<PhysicsComponent>(visitor))
            _visitorPhysics.SetLinearVelocity(visitor, Vector2.Zero);

        _visitorHatches[visitor] = (room.Map, request.Hatch);
        room.EmptySince = null;
        _ui.CloseUi(request.Hatch, PersonalBunkerUiKey.Key, visitor);
    }

    private void UpdateVisitRequests()
    {
        foreach (var request in _visitRequests.Values.ToArray())
        {
            if (!CanResolveVisit(request))
                request.Prompt.Close();
        }

        foreach (var (visitor, origin) in _visitorHatches.ToArray())
        {
            if (!Exists(visitor) || Transform(visitor).MapUid != origin.Map)
                _visitorHatches.Remove(visitor);
        }
    }

    private void ClearVisits()
    {
        var requests = _visitRequests.Values.ToArray();
        _visitRequests.Clear();
        foreach (var request in requests)
            request.Prompt.Close();
        _visitorHatches.Clear();
    }
}
