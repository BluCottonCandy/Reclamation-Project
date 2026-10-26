using System.Linq;
using System.Numerics;
using Content.Shared._Misfits.Expeditions;
using Robust.Server.GameObjects;
using Content.Server.Warps;
using Content.Server.Storage.Components;
using Content.Shared.Doors;
using Content.Shared.Doors.Systems;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Power.Generator;
using Content.Shared.Storage;
using Content.Shared.Throwing;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Random;

namespace Content.Server._ReclamationProject.Expeditions;

/// <summary>Hand-authored missions, independent of salvage generation and station job allocation.</summary>
public sealed class FactoryExpeditionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly WarperSystem _warper = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly ThrownItemSystem _thrown = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SharedPhysicsSystem));
        SubscribeLocalEvent<FactoryDepartureComponent, ActivateInWorldEvent>(OpenDeparture);
        SubscribeLocalEvent<FactoryDepartureComponent, InteractHandEvent>(OpenDepartureByHand);
        SubscribeLocalEvent<FactoryDepartureComponent, N14ExpeditionLaunchMessage>(SelectDeparture);
        SubscribeLocalEvent<FactoryExitComponent, ActivateInWorldEvent>(Exit);
        SubscribeLocalEvent<FactoryExitComponent, ExaminedEvent>(ExamineExit);
        SubscribeLocalEvent<FactoryGeneratorComponent, InteractUsingEvent>(InsertGear);
        SubscribeLocalEvent<FactoryVaultDoorComponent, BeforeDoorOpenedEvent>(BeforeOpen);
        SubscribeLocalEvent<FactoryGearComponent, ContainerGettingInsertedAttemptEvent>(StoreGear);
        SubscribeLocalEvent<FactoryGearComponent, ExaminedEvent>(ExamineGear);
        SubscribeLocalEvent<FactoryGearComponent, ThrownEvent>(OnGearThrown);
        SubscribeLocalEvent<FactoryGearComponent, MapInitEvent>((uid, comp, args) => CheckGearBoundary());
    }

    private void OnGearThrown(EntityUid uid, FactoryGearComponent comp, ref ThrownEvent args)
    {
        comp.ThrowOrigin = _transform.GetMapCoordinates(uid);
    }

    private void LimitGearThrows()
    {
        var gears = EntityQueryEnumerator<FactoryGearComponent, PhysicsComponent>();
        while (gears.MoveNext(out var uid, out var gear, out var body))
        {
            if (gear.ThrowOrigin is not {} origin)
                continue;
            var current = _transform.GetMapCoordinates(uid);
            if (current.MapId != origin.MapId || _containers.IsEntityInContainer(uid)
                || (TryComp<PullableComponent>(uid, out var pulling) && pulling.BeingPulled))
            {
                gear.ThrowOrigin = null;
                continue;
            }
            var offset = current.Position - origin.Position;
            if (offset.LengthSquared() >= 1f)
            {
                // Clamp actual travel, including any sliding after the throw lands.
                _transform.SetMapCoordinates(uid, new MapCoordinates(origin.Position + Vector2.Normalize(offset), origin.MapId));
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
                _physics.SetAngularVelocity(uid, 0f, body: body);
                if (TryComp<ThrownItemComponent>(uid, out var thrown))
                    _thrown.StopThrow(uid, thrown);
                gear.ThrowOrigin = null;
            }
            else if (!body.Awake)
                gear.ThrowOrigin = null;
        }
    }

    private void OpenDepartureByHand(EntityUid uid, FactoryDepartureComponent comp, InteractHandEvent args)
    {
        if (args.Handled)
            return;
        var activate = new ActivateInWorldEvent(args.User, uid, true);
        OpenDeparture(uid, comp, activate);
        args.Handled = activate.Handled;
    }

    private void OpenDeparture(EntityUid uid, FactoryDepartureComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled || !HasComp<HandsComponent>(args.User))
            return;
        args.Handled = true;
        _ui.SetUiState(uid, N14ExpeditionBoardUiKey.Key, new N14ExpeditionBoardState(
            false, null, null,
            new List<N14ExpeditionTierInfo> { new("FactoryEasy", "Easy — Factory", Color.Green, comp.MapPool?.Count ?? 1, comp.Duration / 60f) },
            false, null));
        _ui.TryOpenUi(uid, N14ExpeditionBoardUiKey.Key, args.User);
    }

    private void SelectDeparture(EntityUid uid, FactoryDepartureComponent comp, N14ExpeditionLaunchMessage args)
    {
        if (args.DifficultyId != "FactoryEasy" || !HasComp<HandsComponent>(args.Actor)
            || !_transform.GetMapCoordinates(args.Actor).InRange(_transform.GetMapCoordinates(uid), comp.GatherRadius))
            return;
        Depart(uid, comp, new ActivateInWorldEvent(args.Actor, uid, true));
        _ui.CloseUi(uid, N14ExpeditionBoardUiKey.Key, args.Actor);
    }

    private void CheckGearBoundary()
    {
        var gears = EntityQueryEnumerator<FactoryGearComponent, TransformComponent>();
        while (gears.MoveNext(out var uid, out var gear, out var xform))
        {
            if (gear.ExpeditionMap == null && xform.MapUid is {} map && HasComp<FactoryRunComponent>(map))
                gear.ExpeditionMap = map;
            if (gear.ExpeditionMap != null && xform.MapUid != gear.ExpeditionMap)
                QueueDel(uid);
        }
    }

    private void StoreGear(EntityUid uid, FactoryGearComponent comp, ContainerGettingInsertedAttemptEvent args)
    {
        if (HasComp<StorageComponent>(args.Container.Owner) || HasComp<EntityStorageComponent>(args.Container.Owner))
            args.Cancel();
    }

    private void ExamineGear(EntityUid uid, FactoryGearComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("factory-gear-slowdown-examine"));
    }

    private void BeforeOpen(EntityUid uid, FactoryVaultDoorComponent comp, BeforeDoorOpenedEvent args)
    {
        if (!comp.Unlocked)
            args.Cancel();
    }

    private void InsertGear(EntityUid uid, FactoryGeneratorComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<FactoryGearComponent>(args.Used))
            return;
        args.Handled = true;
        if (comp.Powered)
            return;

        comp.Powered = true;
        _appearance.SetData(uid, GeneratorVisuals.Running, true);
        QueueDel(args.Used);
        var map = Transform(uid).MapID;
        var doors = EntityQueryEnumerator<FactoryVaultDoorComponent, TransformComponent>();
        while (doors.MoveNext(out var door, out var vault, out var xform))
        {
            if (xform.MapID != map)
                continue;
            vault.Unlocked = true;
            _doors.TryOpen(door);
        }
        _popup.PopupEntity(Loc.GetString("factory-generator-started"), uid, args.User, PopupType.Large);
    }

    private void Depart(EntityUid uid, FactoryDepartureComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled || !HasComp<HandsComponent>(args.User))
            return;
        args.Handled = true;
        var origin = _transform.GetMapCoordinates(uid);
        var party = new List<EntityUid>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var xform))
        {
            if (HasComp<HandsComponent>(player) && _transform.GetMapCoordinates(player, xform).InRange(origin, comp.GatherRadius))
                party.Add(player);
        }
        if (!party.Contains(args.User))
            party.Add(args.User);
        var mapPath = comp.MapPool is { Count: > 0 } ? _random.Pick(comp.MapPool) : comp.MapPath;
        if (!_loader.TryLoadMap(mapPath, out var map, out _))
        {
            _popup.PopupEntity(Loc.GetString("factory-launch-failed"), uid, args.User);
            return;
        }

        var run = EnsureComp<FactoryRunComponent>(map.Value.Owner);
        run.Return = Transform(uid).Coordinates;
        run.Deadline = _timing.CurTime + TimeSpan.FromSeconds(comp.Duration);
        _maps.InitializeMap(map.Value.Owner);
        var arrivals = EntityQueryEnumerator<FactoryArrivalComponent, TransformComponent>();
        EntityUid? arrival = null;
        while (arrivals.MoveNext(out var marker, out _, out var xform))
        {
            if (xform.MapID == Transform(map.Value.Owner).MapID)
            {
                arrival = marker;
                break;
            }
        }
        if (arrival == null)
        {
            QueueDel(map.Value.Owner);
            _popup.PopupEntity(Loc.GetString("factory-launch-failed"), uid, args.User);
            return;
        }
        foreach (var player in party)
        {
            if (!_warper.WarpEntityTo(player, arrival.Value))
                continue;
            run.Bodies.Add(player);
            _popup.PopupEntity(Loc.GetString("factory-time-start"), player, player, PopupType.Large);
        }
        if (run.Bodies.Count == 0)
            QueueDel(map.Value.Owner);
    }

    private bool TryGetRun(EntityUid uid, out EntityUid map, out FactoryRunComponent run)
    {
        map = Transform(uid).MapUid ?? EntityUid.Invalid;
        return TryComp(map, out run!);
    }

    private void ExamineExit(EntityUid uid, FactoryExitComponent comp, ExaminedEvent args)
    {
        if (TryGetRun(uid, out _, out var run))
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((run.Deadline - _timing.CurTime).TotalSeconds));
            args.PushMarkup(Loc.GetString("factory-time-remaining", ("minutes", seconds / 60), ("seconds", seconds % 60)));
        }
    }

    private void Exit(EntityUid uid, FactoryExitComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled || !TryGetRun(uid, out _, out var run))
            return;
        args.Handled = true;
        Return(args.User, run);
    }

    private bool Return(EntityUid body, FactoryRunComponent run)
    {
        if (!Exists(body) || !Exists(run.Return.EntityId))
            return false;
        // Bind even newly spawned gear before moving its carrier or anything they pull.
        CheckGearBoundary();
        var marker = Spawn("FactoryReturnMarker", run.Return);
        var success = _warper.WarpEntityTo(body, marker);
        CheckGearBoundary();
        QueueDel(marker);
        return success;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        CheckGearBoundary();
        LimitGearThrows();
        var runs = EntityQueryEnumerator<FactoryRunComponent, TransformComponent>();
        while (runs.MoveNext(out var map, out var run, out var mapTransform))
        {
            var left = (run.Deadline - _timing.CurTime).TotalSeconds;
            foreach (var threshold in new[] {600, 300, 60})
            {
                if (left > 0 && left <= threshold && run.Warnings.Add(threshold))
                {
                    foreach (var body in run.Bodies)
                        if (Exists(body) && Transform(body).MapID == mapTransform.MapID)
                            _popup.PopupEntity(Loc.GetString("factory-time-warning", ("minutes", threshold / 60)), body, body, PopupType.LargeCaution);
                }
            }
            if (left > 0)
                continue;
            if (_timing.CurTime < run.RetryAt)
                continue;
            run.RetryAt = _timing.CurTime + TimeSpan.FromSeconds(1);

            // Include ghosts and any other player bodies that entered the instance.
            // Never delete a map while a player's current body is still on it.
            var minds = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
            var evacuees = run.Bodies.ToHashSet();
            while (minds.MoveNext(out var body, out _, out var xform))
                if (xform.MapID == mapTransform.MapID)
                    evacuees.Add(body);
            var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
            while (actors.MoveNext(out var body, out _, out var xform))
                if (xform.MapID == mapTransform.MapID)
                    evacuees.Add(body);
            var safe = true;
            foreach (var body in evacuees)
            {
                if (!Exists(body) || Transform(body).MapID != mapTransform.MapID)
                    continue;
                if (!run.ExpiryAnnounced)
                    _popup.PopupEntity(Loc.GetString("factory-time-expired"), body, body, PopupType.LargeCaution);
                safe &= Return(body, run);
                safe &= Transform(body).MapID != mapTransform.MapID;
            }
            run.ExpiryAnnounced = true;
            if (safe)
                QueueDel(map);
        }
    }
}
