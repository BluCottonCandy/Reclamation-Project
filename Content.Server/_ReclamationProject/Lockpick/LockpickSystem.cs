using System.Linq;
using Content.Shared._Misfits.Special;
using Content.Shared._ReclamationProject.Lockpick;
using Content.Shared.Access.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Lockpick;

/// <summary>
/// #Reclamation Project - lockpicking for locked doors without power (wooden and metal secure doors).
/// Using a lockpick set on one opens the pin minigame. The server rolls the lock and checks each pin;
/// a picked door opens once. Powered doors (airlocks) are the emag's job.
/// </summary>
public sealed class LockpickSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private static readonly TimeSpan ReleaseCooldown = TimeSpan.FromSeconds(0.25);
    /// <summary>Extra pixels of forgiveness on the server check (lag, rounding).</summary>
    private const float Tolerance = 2f;

    private sealed class Session
    {
        public EntityUid Door;
        public EntityUid User;
        public List<LockPinState> Pins = new();
        public List<int> Order = new();
        public int Next;
        public int Agility;
        public bool Perceptive;
        public int Clicks;
        public string? Message;
        public bool Done;
        public TimeSpan NextRelease;
    }

    private readonly Dictionary<EntityUid, Session> _sessions = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LockpickComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<LockpickComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<LockpickComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<LockpickComponent, LockpickReleaseMessage>(OnRelease);
        SubscribeLocalEvent<LockpickComponent, LockpickResetMessage>(OnReset);
        SubscribeLocalEvent<LockpickComponent, LockpickSnapMessage>(OnSnap);
        SubscribeLocalEvent<LockpickComponent, LockpickSlipMessage>(OnSlip);
        SubscribeLocalEvent<LockpickComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnExamined(Entity<LockpickComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("lockpick-examine", ("picks", ent.Comp.Picks)));
    }

    /// <summary>A closed door with a working lock and no power system: wooden and metal secure doors.</summary>
    public bool IsPickable(EntityUid door)
    {
        return TryComp<DoorComponent>(door, out var doorComp) &&
               doorComp.State == DoorState.Closed &&
               !HasComp<AirlockComponent>(door) &&
               TryComp<AccessReaderComponent>(door, out var reader) &&
               reader.Enabled &&
               (reader.AccessLists.Count > 0 || reader.AccessKeys.Count > 0);
    }

    private void OnAfterInteract(Entity<LockpickComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } door)
            return;

        if (HasComp<AirlockComponent>(door))
        {
            _popup.PopupEntity(Loc.GetString("lockpick-powered"), door, args.User);
            args.Handled = true;
            return;
        }

        if (!HasComp<DoorComponent>(door))
            return;

        args.Handled = true;
        if (!IsPickable(door))
        {
            _popup.PopupEntity(Loc.GetString("lockpick-not-locked"), door, args.User);
            return;
        }

        Start(ent, door, args.User);
    }

    public void Start(Entity<LockpickComponent> pick, EntityUid door, EntityUid user)
    {
        var session = new Session
        {
            Door = door,
            User = user,
            Agility = _special.UsesSpecialStats(user) ? _special.GetEffective(user, SpecialStat.Agility) : 5,
            Perceptive = _special.UsesSpecialStats(user) && _special.GetEffective(user, SpecialStat.Perception) >= LockpickGeometry.PerceptionHint,
        };

        var (pins, spools, serrated) = Difficulty(door);
        var types = Enumerable.Repeat(LockPinType.Plain, pins).ToArray();
        var slots = Enumerable.Range(0, pins).OrderBy(_ => _random.Next()).ToList();
        for (var i = 0; i < spools && slots.Count > 0; i++)
        {
            types[slots[0]] = LockPinType.Spool;
            slots.RemoveAt(0);
        }
        for (var i = 0; i < serrated && slots.Count > 0; i++)
        {
            types[slots[0]] = LockPinType.Serrated;
            slots.RemoveAt(0);
        }

        for (var i = 0; i < pins; i++)
        {
            session.Pins.Add(new LockPinState { Length = _random.Next(42, 77), Type = types[i] });
        }
        session.Order = Enumerable.Range(0, pins).OrderBy(_ => _random.Next()).ToList();

        _sessions[pick] = session;
        _ui.OpenUi(pick.Owner, LockpickUiKey.Key, user);
        Send(pick, session);
    }

    /// <summary>For tests and admins: the lock being picked with this pick set, and its binding order.</summary>
    public bool TryGetLock(EntityUid pick, out IReadOnlyList<LockPinState> pins, out IReadOnlyList<int> order, out int next)
    {
        pins = Array.Empty<LockPinState>();
        order = Array.Empty<int>();
        next = 0;
        if (!_sessions.TryGetValue(pick, out var session))
            return false;

        pins = session.Pins;
        order = session.Order;
        next = session.Next;
        return true;
    }

    private (int Pins, int Spools, int Serrated) Difficulty(EntityUid door)
    {
        if (TryComp<LockpickDifficultyComponent>(door, out var difficulty))
            return (Math.Clamp(difficulty.Pins, 1, 6), difficulty.Spools, difficulty.Serrated);

        // Wooden doors get a cheap three-pin lock; everything else is a proper five-pin with security pins.
        if (TryComp<DamageableComponent>(door, out var damageable) && damageable.DamageModifierSetId == "Wood")
            return (3, 0, 0);

        return (5, 1, 1);
    }

    private void Send(Entity<LockpickComponent> pick, Session session)
    {
        _ui.SetUiState(pick.Owner, LockpickUiKey.Key, new LockpickBuiState
        {
            Pins = session.Pins.Select(p => new LockPinState
            {
                Length = p.Length,
                Type = p.Type,
                Set = p.Set,
                Jammed = p.Jammed,
                FalseSet = p.FalseSet,
            }).ToList(),
            Picks = pick.Comp.Picks,
            Agility = session.Agility,
            Binding = session.Perceptive && !session.Done && session.Next < session.Order.Count ? session.Order[session.Next] : null,
            Stiff = !session.Done && session.Next < session.Order.Count ? session.Order[session.Next] : -1,
            Done = session.Done,
            Message = session.Message,
            Clicks = session.Clicks,
        });
    }

    private bool TryGetSession(Entity<LockpickComponent> pick, EntityUid actor, out Session session)
    {
        return _sessions.TryGetValue(pick, out session!) && session.User == actor && !session.Done;
    }

    private void OnRelease(Entity<LockpickComponent> ent, ref LockpickReleaseMessage msg)
    {
        if (!TryGetSession(ent, msg.Actor, out var session) || msg.Pin < 0 || msg.Pin >= session.Pins.Count)
            return;

        var now = _timing.CurTime;
        if (now < session.NextRelease)
            return;
        session.NextRelease = now + ReleaseCooldown;

        var pin = session.Pins[msg.Pin];
        if (pin.Set || pin.Jammed)
            return;

        if (session.Order[session.Next] != msg.Pin)
        {
            session.Message = Loc.GetString("lockpick-not-binding");
            Send(ent, session);
            return;
        }

        var lift = Math.Clamp(msg.Lift, 0f, LockpickGeometry.Rest);
        var top = LockpickGeometry.Top(pin.Length, lift);
        var half = LockpickGeometry.Band(session.Agility, pin.Type) / 2f + Tolerance;

        if (top < LockpickGeometry.Shear - half)
        {
            pin.Jammed = true;
            session.Message = Loc.GetString("lockpick-overset");
        }
        else if (top > LockpickGeometry.Shear + half)
        {
            session.Message = Loc.GetString(pin.Type == LockPinType.Serrated ? "lockpick-ridge" : "lockpick-too-low");
        }
        else if (pin.Type == LockPinType.Spool && !pin.FalseSet)
        {
            pin.FalseSet = true;
            session.Clicks++;
            session.Message = Loc.GetString("lockpick-false-set");
        }
        else if (pin.Type == LockPinType.Spool && msg.Tension != LockpickTension.Light)
        {
            session.Message = Loc.GetString("lockpick-spool-push");
        }
        else
        {
            pin.Set = true;
            pin.FalseSet = false;
            session.Next++;
            session.Clicks++;
            session.Message = Loc.GetString(pin.Type switch
            {
                LockPinType.Spool => "lockpick-spool-set",
                LockPinType.Serrated => "lockpick-serrated-set",
                _ => "lockpick-set",
            });

            if (session.Next >= session.Pins.Count)
            {
                Unlock(ent, session);
                return;
            }
        }

        Send(ent, session);
    }

    private void Unlock(Entity<LockpickComponent> pick, Session session)
    {
        session.Done = true;
        session.Message = Loc.GetString("lockpick-unlocked");
        Send(pick, session);

        if (TryComp<DoorComponent>(session.Door, out var door) && door.State == DoorState.Closed)
            _doors.StartOpening(session.Door, door, session.User);

        _popup.PopupEntity(Loc.GetString("lockpick-unlocked-popup"), session.Door, session.User);
        _sessions.Remove(pick);
        _ui.CloseUi(pick.Owner, LockpickUiKey.Key, session.User);
    }

    private void DropAll(Session session)
    {
        foreach (var pin in session.Pins)
        {
            pin.Set = false;
            pin.Jammed = false;
            pin.FalseSet = false;
        }
        session.Next = 0;
    }

    private void OnReset(Entity<LockpickComponent> ent, ref LockpickResetMessage msg)
    {
        if (!TryGetSession(ent, msg.Actor, out var session))
            return;

        DropAll(session);
        session.Message = Loc.GetString("lockpick-reset");
        Send(ent, session);
    }

    private void OnSnap(Entity<LockpickComponent> ent, ref LockpickSnapMessage msg)
    {
        if (!TryGetSession(ent, msg.Actor, out var session))
            return;

        ent.Comp.Picks--;
        Dirty(ent);
        DropAll(session);

        if (ent.Comp.Picks <= 0)
        {
            _popup.PopupEntity(Loc.GetString("lockpick-all-broken"), session.User, session.User, PopupType.MediumCaution);
            End(ent);
            QueueDel(ent);
            return;
        }

        session.Message = Loc.GetString("lockpick-snap");
        Send(ent, session);
    }

    private void OnSlip(Entity<LockpickComponent> ent, ref LockpickSlipMessage msg)
    {
        if (!TryGetSession(ent, msg.Actor, out var session) || session.Next == 0)
            return;

        // A spool caught in a false set holds everything in place.
        if (session.Pins.Any(p => p.FalseSet))
            return;

        session.Next--;
        session.Pins[session.Order[session.Next]].Set = false;
        session.Message = Loc.GetString("lockpick-slip");
        Send(ent, session);
    }

    private void OnClosed(Entity<LockpickComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is LockpickUiKey)
            _sessions.Remove(ent);
    }

    private void OnShutdown(Entity<LockpickComponent> ent, ref ComponentShutdown args)
    {
        _sessions.Remove(ent);
    }

    private void End(Entity<LockpickComponent> pick)
    {
        if (!_sessions.Remove(pick, out var session))
            return;
        _ui.CloseUi(pick.Owner, LockpickUiKey.Key, session.User);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_sessions.Count == 0)
            return;

        foreach (var (pick, session) in _sessions.ToArray())
        {
            if (Deleted(pick) || Deleted(session.Door) || Deleted(session.User) ||
                !TryComp<LockpickComponent>(pick, out var comp) ||
                !_hands.IsHolding(session.User, pick) ||
                !_blocker.CanInteract(session.User, session.Door) ||
                !IsPickable(session.Door) ||
                !_transform.InRange(session.User, session.Door, comp.Range))
            {
                if (!Deleted(session.User) && !Deleted(session.Door))
                    _popup.PopupEntity(Loc.GetString("lockpick-interrupted"), session.User, session.User);
                if (TryComp<LockpickComponent>(pick, out var c))
                    End((pick, c));
                else
                    _sessions.Remove(pick);
            }
        }
    }
}
