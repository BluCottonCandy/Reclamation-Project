using System.Linq;
using Content.Shared._ReclamationProject.Institute.Relay;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Institute.Relay;

/// <summary>
/// #Reclamation Project - Gen 3 synths and Coursers relay onto Institute relay pads.
/// The action opens a drop-down of every deployed pad; picking one relays the user and their grab/carry chain onto it.
/// Same relay rules as the implant: passengers get sick, and relaying again inside the cooldown makes the user sick.
/// </summary>
public sealed class InstituteRelayPadSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly InstituteRelaySystem _relay = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InstitutePadTravelerComponent, MapInitEvent>(OnTravelerInit);
        SubscribeLocalEvent<InstitutePadTravelerComponent, ComponentShutdown>(OnTravelerShutdown);
        SubscribeLocalEvent<InstitutePadTravelerComponent, InstituteOpenPadListEvent>(OnOpenList);
        SubscribeNetworkEvent<InstitutePadRelayRequestEvent>(OnRelayRequest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<InstitutePadTravelerComponent>();
        while (query.MoveNext(out var uid, out var traveler))
        {
            if (!traveler.CoolingDown || traveler.LastUse + traveler.Cooldown > now)
                continue;

            traveler.CoolingDown = false;
            _alerts.ClearAlert(uid, traveler.CooldownAlert);
            _popup.PopupEntity(Loc.GetString("institute-pad-ready"), uid, uid);
        }
    }

    private void OnTravelerInit(Entity<InstitutePadTravelerComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnTravelerShutdown(Entity<InstitutePadTravelerComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnOpenList(Entity<InstitutePadTravelerComponent> ent, ref InstituteOpenPadListEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(ent, out var actor))
            return;

        args.Handled = true;
        SendList(ent, actor.PlayerSession);
    }

    public List<InstitutePadEntry> GetPads(EntityUid user)
    {
        var from = _transform.GetMapCoordinates(user);
        var pads = new List<(InstitutePadEntry Entry, float Distance)>();
        var query = EntityQueryEnumerator<InstituteRelayPadComponent, TransformComponent>();
        while (query.MoveNext(out var pad, out _, out var xform))
        {
            if (!xform.Anchored)
                continue;

            var to = _transform.GetMapCoordinates(pad, xform);
            var entry = new InstitutePadEntry { Pad = GetNetEntity(pad), Name = Name(pad) };
            var distance = float.MaxValue;
            if (to.MapId == from.MapId)
            {
                var offset = to.Position - from.Position;
                distance = offset.Length();
                entry.Where = Loc.GetString("institute-pad-where",
                    ("distance", (int) MathF.Round(distance)),
                    ("direction", Loc.GetString($"zzzz-fmt-direction-{offset.GetDir()}")));
            }
            else
            {
                entry.Where = Loc.GetString("institute-pad-other-level");
            }

            pads.Add((entry, distance));
        }

        return pads.OrderBy(p => p.Distance).ThenBy(p => p.Entry.Name).Select(p => p.Entry).ToList();
    }

    private void SendList(EntityUid user, ICommonSession session, string? status = null)
    {
        RaiseNetworkEvent(new InstitutePadListEvent { Pads = GetPads(user), Status = status }, session);
    }

    private void OnRelayRequest(InstitutePadRelayRequestEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !TryComp<InstitutePadTravelerComponent>(user, out var traveler))
            return;

        var pad = GetEntity(msg.Pad);
        if (!HasComp<InstituteRelayPadComponent>(pad) || TerminatingOrDeleted(pad) || !Transform(pad).Anchored)
        {
            SendList(user, args.SenderSession, Loc.GetString("institute-pad-gone"));
            return;
        }

        TryRelayToPad((user, traveler), pad);
    }

    /// <summary>Relays the traveler (and their grab/carry chain) onto the pad.</summary>
    public bool TryRelayToPad(Entity<InstitutePadTravelerComponent> user, EntityUid pad)
    {
        if (!_mobState.IsAlive(user) || !_relay.TryInstantRelay(user, Transform(pad).Coordinates))
            return false;

        var now = _timing.CurTime;
        if (user.Comp.LastUse is { } last && now - last < user.Comp.Cooldown)
            _relay.ApplyEarlyPenalty(user, user.Comp.OrganicPenalty, user.Comp.RobotPenalty);

        user.Comp.LastUse = now;
        user.Comp.CoolingDown = true;
        _alerts.ShowAlert(user, user.Comp.CooldownAlert, cooldown: (now, now + user.Comp.Cooldown), autoRemove: true);
        return true;
    }
}
