using Content.Server._MultiZ.Core;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Carrying;
using Content.Server.Medical;
using Content.Shared._ReclamationProject.Institute.Relay;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Institute.Relay;

/// <summary>
/// Institute relay: the body poofs into an unseen signal that flies anywhere (any level),
/// then relays in where the signal is, or cancels back to where it started.
/// Everyone the user is grabbing or carrying (and everyone they grab, down the chain) comes along.
/// The relay is a head organ (slot <see cref="InstituteRelayImplantComponent.SlotId"/>), so surgery can take it out.
/// </summary>
public sealed class InstituteRelaySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private CarryingSystem _carrying = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VomitSystem _vomit = default!;
    // Concrete server MZSystem: the abstract MZSharedSystem is not injectable.
    [Dependency] private MZSystem _zLevel = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyPartComponent, ComponentStartup>(OnPartStartup);
        SubscribeLocalEvent<InstituteRelayImplantComponent, OrganAddedToBodyEvent>(OnAddedToBody);
        SubscribeLocalEvent<InstituteRelayImplantComponent, OrganRemovedFromBodyEvent>(OnRemovedFromBody);
        SubscribeLocalEvent<InstituteRelayImplantComponent, InstituteRelayStartEvent>(OnStart);

        SubscribeLocalEvent<InstituteRelayEyeComponent, ComponentInit>(OnEyeInit);
        SubscribeLocalEvent<InstituteRelayEyeComponent, EntityTerminatingEvent>(OnEyeTerminating);
        SubscribeLocalEvent<InstituteRelayEyeComponent, InstituteRelayArriveEvent>(OnArrive);
        SubscribeLocalEvent<InstituteRelayEyeComponent, InstituteRelayCancelEvent>(OnCancel);
        SubscribeLocalEvent<InstituteRelayEyeComponent, InstituteRelayUpEvent>(OnUp);
        SubscribeLocalEvent<InstituteRelayEyeComponent, InstituteRelayDownEvent>(OnDown);

        SubscribeLocalEvent<InstituteRelayRiderComponent, PlayerDetachedEvent>(OnRiderDetached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<InstituteRelayImplantComponent, OrganComponent>();
        while (query.MoveNext(out _, out var relay, out var organ))
        {
            if (!relay.CoolingDown || relay.LastArrival + relay.Cooldown > now)
                continue;

            relay.CoolingDown = false;
            if (organ.Body is not { } owner || Deleted(owner))
                continue;

            _alerts.ClearAlert(owner, relay.CooldownAlert);
            _popup.PopupEntity(Loc.GetString("institute-relay-ready"), owner, owner);
        }
    }

    /// <summary>
    /// Every head gets an empty relay slot, so the implant can be put in (and taken out) by surgery.
    /// </summary>
    private void OnPartStartup(Entity<BodyPartComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.PartType != BodyPartType.Head || ent.Comp.Organs.ContainsKey(InstituteRelayImplantComponent.SlotId))
            return;

        if (_body.TryCreateOrganSlot(ent, InstituteRelayImplantComponent.SlotId, out _, ent.Comp))
            Dirty(ent);
    }

    /// <summary>
    /// Puts a relay implant into the body's head. Used by the Institute job specials.
    /// </summary>
    public EntityUid? TryAddRelayOrgan(EntityUid body, EntProtoId implant)
    {
        foreach (var (head, part) in _body.GetBodyChildrenOfType(body, BodyPartType.Head))
        {
            _body.TryCreateOrganSlot(head, InstituteRelayImplantComponent.SlotId, out _, part);
            var organ = Spawn(implant, Transform(body).Coordinates);
            if (_body.InsertOrgan(head, organ, InstituteRelayImplantComponent.SlotId, part))
                return organ;

            Del(organ);
        }

        return null;
    }

    private void OnAddedToBody(Entity<InstituteRelayImplantComponent> ent, ref OrganAddedToBodyEvent args)
    {
        var target = args.Body;
        if (!_faction.IsMember(target, ent.Comp.Faction))
        {
            FryBrain(ent, target);
            return;
        }

        if (ent.Comp.CanSelfRelay)
            _actions.AddAction(target, ref ent.Comp.ActionEntity, ent.Comp.Action, ent);
    }

    private void OnRemovedFromBody(Entity<InstituteRelayImplantComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        _actions.RemoveProvidedActions(args.OldBody, ent);
        _alerts.ClearAlert(args.OldBody, ent.Comp.CooldownAlert);
    }

    private void FryBrain(Entity<InstituteRelayImplantComponent> ent, EntityUid target)
    {
        if (!_thresholds.TryGetThresholdForState(target, MobState.Dead, out var dead))
            return;

        _popup.PopupEntity(Loc.GetString("institute-relay-wrong-head"), target, target, PopupType.LargeCaution);

        var heat = new DamageSpecifier { DamageDict = { ["Heat"] = dead.Value + ent.Comp.WrongHeadExtraHeat } };
        _damage.TryChangeDamage(target, heat, ignoreResistances: true, origin: ent);
    }

    private void OnStart(Entity<InstituteRelayImplantComponent> ent, ref InstituteRelayStartEvent args)
    {
        if (args.Handled || !ent.Comp.CanSelfRelay)
            return;

        args.Handled = TryStartRelay(args.Performer, ent);
    }

    /// <summary>
    /// Poofs <paramref name="user"/> and their grab/carry chain into a relay signal.
    /// Public so a relay pad console can start one later.
    /// </summary>
    public bool TryStartRelay(EntityUid user, EntityUid? implant = null)
    {
        if (HasComp<InstituteRelayRiderComponent>(user))
            return false;

        if (_container.IsEntityInContainer(user) || _buckle.IsBuckled(user))
        {
            _popup.PopupEntity(Loc.GetString("institute-relay-blocked"), user, user);
            return false;
        }

        var passengers = GetGrabChain(user);

        var start = Transform(user).Coordinates;
        var mapStart = _transform.GetMapCoordinates(user);
        var eyeProto = CompOrNull<InstituteRelayImplantComponent>(implant)?.EyePrototype ?? "InstituteRelayEye";
        var eye = Spawn(eyeProto, mapStart);
        var comp = Comp<InstituteRelayEyeComponent>(eye);

        // Let go of everyone first; the container would break grabs and carries messily otherwise.
        foreach (var body in passengers)
        {
            if (TryComp<CarryingComponent>(body, out var carrying))
                _carrying.DropCarried(body, carrying.Carried);
            if (TryComp<PullableComponent>(body, out var pullable) && pullable.Puller != null)
                _pulling.TryStopPull(body, pullable);
        }
        if (TryComp<CarryingComponent>(user, out var userCarrying))
            _carrying.DropCarried(user, userCarrying.Carried);

        if (!_container.Insert(user, comp.Bodies))
        {
            Del(eye);
            _popup.PopupEntity(Loc.GetString("institute-relay-blocked"), user, user);
            return false;
        }

        Spawn(comp.OutEffect, mapStart);
        _audio.PlayPvs(comp.OutSound, start);
        HideEye(comp, user);

        foreach (var body in passengers)
        {
            var from = _transform.GetMapCoordinates(body);
            if (!_container.Insert(body, comp.Bodies))
                continue;

            Spawn(comp.OutEffect, from);
            HideEye(comp, body);
            comp.Passengers.Add(body);
        }

        comp.Rider = user;
        comp.Implant = implant;
        comp.Start = start;
        EnsureComp<InstituteRelayRiderComponent>(user).Eye = eye;

        _mover.SetRelay(user, eye);

        _actions.AddAction(user, ref comp.ArriveActionEntity, comp.ArriveAction, eye);
        _actions.AddAction(user, ref comp.CancelActionEntity, comp.CancelAction, eye);
        _actions.AddAction(user, ref comp.UpActionEntity, comp.UpAction, eye);
        _actions.AddAction(user, ref comp.DownActionEntity, comp.DownAction, eye);

        _popup.PopupEntity(Loc.GetString("institute-relay-start"), user, user);
        return true;
    }

    /// <summary>
    /// Everything the user drags or carries (people, crates, anything pullable), then everything
    /// those drag or carry, and so on.
    /// </summary>
    private List<EntityUid> GetGrabChain(EntityUid user)
    {
        var chain = new List<EntityUid>();
        var seen = new HashSet<EntityUid> { user };
        var queue = new Queue<EntityUid>();
        queue.Enqueue(user);

        while (queue.TryDequeue(out var holder))
        {
            if (TryComp<PullerComponent>(holder, out var puller))
                TryAddToChain(puller.Pulling);
            if (TryComp<CarryingComponent>(holder, out var carrying))
                TryAddToChain(carrying.Carried);
        }

        return chain;

        void TryAddToChain(EntityUid? held)
        {
            if (held is not { } body || !seen.Add(body) || Transform(body).Anchored)
                return;

            chain.Add(body);
            queue.Enqueue(body);
        }
    }

    private void HideEye(InstituteRelayEyeComponent comp, EntityUid body)
    {
        if (!TryComp<EyeComponent>(body, out var bodyEye))
            return;

        comp.SavedEyes[body] = (bodyEye.DrawFov, bodyEye.DrawLight);
        _eye.SetDrawFov(body, false, bodyEye);
        _eye.SetDrawLight((body, bodyEye), false);
    }

    private void RestoreEye(InstituteRelayEyeComponent comp, EntityUid body)
    {
        if (!comp.SavedEyes.Remove(body, out var saved) || !TryComp<EyeComponent>(body, out var bodyEye))
            return;

        _eye.SetDrawFov(body, saved.Fov, bodyEye);
        _eye.SetDrawLight((body, bodyEye), saved.Light);
    }

    private void OnEyeInit(Entity<InstituteRelayEyeComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Bodies = _container.EnsureContainer<Container>(ent, InstituteRelayEyeComponent.BodyContainerId);
    }

    private void OnArrive(Entity<InstituteRelayEyeComponent> ent, ref InstituteRelayArriveEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Finish(ent, Transform(ent).Coordinates, arrived: true);
    }

    private void OnCancel(Entity<InstituteRelayEyeComponent> ent, ref InstituteRelayCancelEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Finish(ent, ReturnPoint(ent), arrived: false);
    }

    private void OnUp(Entity<InstituteRelayEyeComponent> ent, ref InstituteRelayUpEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveUp(ent);
    }

    private void OnDown(Entity<InstituteRelayEyeComponent> ent, ref InstituteRelayDownEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveDown(ent);
    }

    private void OnRiderDetached(Entity<InstituteRelayRiderComponent> ent, ref PlayerDetachedEvent args)
    {
        if (TryComp<InstituteRelayEyeComponent>(ent.Comp.Eye, out var eye))
            Finish((ent.Comp.Eye.Value, eye), ReturnPoint((ent.Comp.Eye.Value, eye)), arrived: false);
    }

    private void OnEyeTerminating(Entity<InstituteRelayEyeComponent> ent, ref EntityTerminatingEvent args)
    {
        // Never let the bodies get deleted with the signal.
        if (ent.Comp.Rider != null)
            Finish(ent, Transform(ent).Coordinates, arrived: false, deleteEye: false);
    }

    private EntityCoordinates ReturnPoint(Entity<InstituteRelayEyeComponent> ent)
    {
        return ent.Comp.Start.IsValid(EntityManager) ? ent.Comp.Start : Transform(ent).Coordinates;
    }

    private void Finish(Entity<InstituteRelayEyeComponent> ent, EntityCoordinates destination, bool arrived, bool deleteEye = true)
    {
        if (ent.Comp.Rider is not { } user)
            return;

        ent.Comp.Rider = null;
        _actions.RemoveProvidedActions(user, ent);
        RemComp<RelayInputMoverComponent>(user);
        RemComp<InstituteRelayRiderComponent>(user);

        DropOff(ent.Comp, user, destination);
        _audio.PlayPvs(ent.Comp.InSound, user);
        foreach (var passenger in ent.Comp.Passengers)
        {
            if (!Deleted(passenger))
                DropOff(ent.Comp, passenger, destination);
        }

        // Passengers feel every relay, early or not; cancelling never moved them anywhere.
        if (arrived)
        {
            foreach (var passenger in ent.Comp.Passengers)
            {
                // Dragged crates and other objects ride along but don't get sick.
                if (!Deleted(passenger) && HasComp<MobStateComponent>(passenger))
                    ApplyPassengerPenalty(passenger, ent.Comp);
            }
        }

        ent.Comp.Passengers.Clear();

        if (arrived && TryComp<InstituteRelayImplantComponent>(ent.Comp.Implant, out var implant))
        {
            var now = _timing.CurTime;
            if (implant.LastArrival is { } last && now - last < implant.Cooldown)
                ApplyEarlyPenalty(user, implant);

            implant.LastArrival = now;

            if (implant.CanSelfRelay)
            {
                implant.CoolingDown = true;
                _alerts.ShowAlert(user, implant.CooldownAlert, cooldown: (now, now + implant.Cooldown), autoRemove: true);
            }
        }

        if (deleteEye)
            QueueDel(ent);
    }

    private void DropOff(InstituteRelayEyeComponent comp, EntityUid body, EntityCoordinates destination)
    {
        _container.Remove(body, comp.Bodies, force: true);
        _transform.SetCoordinates(body, destination);
        _transform.AttachToGridOrMap(body);
        RestoreEye(comp, body);
        Spawn(comp.InEffect, _transform.GetMapCoordinates(body));
    }

    /// <summary>
    /// Worn gear with <see cref="InstituteRelayShieldComponent"/> (the Institute MOD suits) blocks
    /// all relay side effects: no sickness and no damage.
    /// </summary>
    private bool IsShielded(EntityUid body)
    {
        if (!_inventory.TryGetContainerSlotEnumerator(body, out var slots))
            return false;

        while (slots.MoveNext(out var slot))
        {
            if (HasComp<InstituteRelayShieldComponent>(slot.ContainedEntity))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Shows the "suit absorbed it" popup and returns true when the body is shielded.
    /// </summary>
    private bool TryShield(EntityUid body)
    {
        if (!IsShielded(body))
            return false;

        _popup.PopupEntity(Loc.GetString("institute-relay-shielded"), body, body);
        return true;
    }

    private bool HasStomach(EntityUid body)
    {
        return _body.TryGetBodyOrganComponents<StomachComponent>(body, out var stomachs) && stomachs.Count > 0;
    }

    private void ApplyEarlyPenalty(EntityUid user, InstituteRelayImplantComponent implant)
    {
        if (TryShield(user))
            return;

        // Vomit needs a stomach, so anything without one (robots) gets shocked instead.
        if (HasStomach(user))
        {
            _popup.PopupEntity(Loc.GetString("institute-relay-sick"), user, user, PopupType.MediumCaution);
            _vomit.Vomit(user);
            _damage.TryChangeDamage(user, implant.OrganicPenalty, ignoreResistances: true);
            return;
        }

        _popup.PopupEntity(Loc.GetString("institute-relay-overload"), user, user, PopupType.MediumCaution);
        _damage.TryChangeDamage(user, implant.RobotPenalty, ignoreResistances: true);
    }

    private void ApplyPassengerPenalty(EntityUid passenger, InstituteRelayEyeComponent comp)
    {
        if (TryShield(passenger))
            return;

        var hurt = _random.Prob(comp.PassengerDamageChance);

        if (HasStomach(passenger))
        {
            _popup.PopupEntity(Loc.GetString("institute-relay-passenger-sick"), passenger, passenger, PopupType.MediumCaution);
            _vomit.Vomit(passenger);
            if (hurt)
                _damage.TryChangeDamage(passenger, comp.PassengerPenalty, ignoreResistances: true);
            return;
        }

        if (hurt)
        {
            _popup.PopupEntity(Loc.GetString("institute-relay-overload"), passenger, passenger, PopupType.MediumCaution);
            _damage.TryChangeDamage(passenger, comp.PassengerRobotPenalty, ignoreResistances: true);
        }
    }
}
