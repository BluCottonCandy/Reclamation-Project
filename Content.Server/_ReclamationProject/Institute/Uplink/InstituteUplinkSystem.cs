using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Objectives;
using Content.Server.Popups;
using Content.Server.Roles;
using Content.Server.Store.Systems;
using Content.Server.StoreDiscount.Systems;
using Content.Shared._ReclamationProject.Institute.Uplink;
using Content.Shared.Chat;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles.Jobs;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.Institute.Uplink;

/// <summary>
/// #Reclamation Project - Institute uplink implant.
/// Every Institute job spawns with a PIN-locked uplink implant and is an Institute agent (antag).
/// Research Points are spent in the uplink; the Director and Division Heads hand out objectives that pay them.
/// </summary>
public sealed partial class InstituteUplinkSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RoleSystem _roles = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly StoreDiscountSystem _discounts = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly InstituteTabletLockSystem _tabletLock = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public static readonly ProtoId<CurrencyPrototype> Currency = "ResearchPoint";
    public static readonly EntProtoId ImplantProto = "InstituteUplinkImplant";
    public static readonly EntProtoId RelayIn = "InstituteRelayInEffect";
    public static readonly EntProtoId RelayOut = "InstituteRelayOutEffect";
    public const string Director = "InstituteDirector";
    public const string DivisionHead = "InstituteDivisionHead";

    private static readonly TimeSpan PinCooldown = TimeSpan.FromSeconds(2);
    private static readonly Color InstituteColor = Color.FromHex("#4fd2e0");

    private EntityUid? _rule;
    private TimeSpan _nextTick;
    private TimeSpan _nextSynthSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevel);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeLocalEvent<InstituteUplinkComponent, MapInitEvent>(OnUplinkInit);
        SubscribeLocalEvent<InstituteUplinkComponent, OpenInstituteUplinkEvent>(OnOpenUplink);
        SubscribeLocalEvent<InstituteTabletLockComponent, InstituteTabletLockedEvent>(OnTabletLocked);
        SubscribeLocalEvent<InstituteMemberComponent, GetBriefingEvent>(OnBriefing);
        SubscribeLocalEvent<InstituteRuleComponent, ObjectivesTextGetInfoEvent>(OnObjectivesText);
        SubscribeLocalEvent<StoreBuyFinishedEvent>(OnBuyFinished);
        SubscribeNetworkEvent<InstitutePinSubmitEvent>(OnPinSubmit);

        InitializeNetwork();
        InitializeSupport();
        InitializeStock();
        InitializeBoard();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextTick)
            return;
        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(1);

        UpdatePayouts();
        UpdateSupport();

        if (_timing.CurTime >= _nextSynthSweep)
        {
            _nextSynthSweep = _timing.CurTime + SynthSweepInterval;
            EnsureSynthUplinks();
        }
    }

    #region Round and rule

    private void OnRunLevel(GameRunLevelChangedEvent args)
    {
        if (args.New == GameRunLevel.PreRoundLobby)
            _rule = null;
        else if (args.New == GameRunLevel.InRound)
            EnsureRule();
    }

    private EntityUid EnsureRule()
    {
        if (_rule is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        _rule = _ticker.AddGameRule("InstituteAgents");
        _ticker.StartGameRule(_rule.Value);
        return _rule.Value;
    }

    private InstituteRuleComponent? RuleComp()
    {
        return _rule is { } rule && TryComp<InstituteRuleComponent>(rule, out var comp) ? comp : null;
    }

    private void OnObjectivesText(Entity<InstituteRuleComponent> ent, ref ObjectivesTextGetInfoEvent args)
    {
        var query = EntityQueryEnumerator<InstituteMemberComponent, MindComponent>();
        while (query.MoveNext(out var mindId, out _, out var mind))
        {
            args.Minds.Add((mindId, mind.CharacterName ?? Loc.GetString("institute-uplink-unknown")));
        }
        args.AgentName = Loc.GetString("institute-agent-name");
    }

    #endregion

    #region Spawn setup

    /// <summary>
    /// Called by the job special while the body is being equipped. The mind is attached later.
    /// </summary>
    public EntityUid? GiveUplink(EntityUid body, int balance)
    {
        if (FindImplant(body) is { } existing)
            return existing;

        if (_implants.AddImplant(body, ImplantProto) is not { } implant ||
            !TryComp<StoreComponent>(implant, out var store) ||
            !TryComp<InstituteUplinkComponent>(implant, out var uplink))
            return null;

        store.AccountOwner = body;
        store.Balance.Clear();
        _store.TryAddCurrency(new Dictionary<string, FixedPoint2> { { Currency, balance } }, implant, store);
        uplink.Pin = NewPin();

        var listings = _store.GetAvailableListings(body, implant, store).ToArray();
        _discounts.InitializeStoreDiscounts(implant, listings, true);
        RefreshStock(implant, store);
        return implant;
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null || !_minds.TryGetMind(args.Mob, out var mindId, out var mind))
            return;

        // #Reclamation Project - Gen 3 synths and Coursers in a non-Institute job (undercover) still belong to
        // the Institute: they get an empty uplink so command can see them and give them orders.
        if (FindImplant(args.Mob) is not { } implant)
        {
            if (!IsInstituteSynth(args.Mob) || GiveUplink(args.Mob, 0) is not { } undercover)
                return;
            implant = undercover;
        }

        RegisterMember(mindId, mind, implant, args.JobId);
    }

    private static readonly HashSet<string> InstituteSynthSpecies = new() { "Gen3Synth", "Courser" };
    private static readonly TimeSpan SynthSweepInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// #Reclamation Project - every Gen 3 synth and Courser with a player in it has an uplink and is on the
    /// Institute network, however they got that body: round start, late join, ghost role, admin spawn, cloning,
    /// body swap, a species change, or the implant being cut out. Runs every couple of seconds.
    /// </summary>
    public void EnsureSynthUplinks()
    {
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, MindContainerComponent>();
        while (query.MoveNext(out var body, out var humanoid, out var container))
        {
            if (!InstituteSynthSpecies.Contains(humanoid.Species) ||
                container.Mind is not { } mindId ||
                !TryComp<MindComponent>(mindId, out var mind))
                continue;

            EnsureSynthUplink(body, mindId, mind);
        }
    }

    private void EnsureSynthUplink(EntityUid body, EntityUid mindId, MindComponent mind)
    {
        if ((FindImplant(body) ?? GiveUplink(body, 0)) is not { } implant)
            return;

        if (TryComp<InstituteMemberComponent>(mindId, out var member) && member.Implant == implant)
            return;

        // Keep the job they already have on the network, else use their real job (none for most ghost roles).
        var job = member?.Job.Id;
        if (string.IsNullOrEmpty(job) && _jobs.MindTryGetJob(mindId, out var jobComp, out _))
            job = jobComp.Prototype?.Id;

        RegisterMember(mindId, mind, implant, job ?? string.Empty);
    }

    private bool IsInstituteSynth(EntityUid body)
    {
        return TryComp<HumanoidAppearanceComponent>(body, out var humanoid) &&
               InstituteSynthSpecies.Contains(humanoid.Species);
    }

    /// <summary>Makes the mind an Institute agent and ties the implant's PIN to it.</summary>
    public void RegisterMember(EntityUid mindId, MindComponent mind, EntityUid implant, string job)
    {
        if (!TryComp<InstituteUplinkComponent>(implant, out var uplink))
            return;

        uplink.OwnerMind = mindId;
        var member = EnsureComp<InstituteMemberComponent>(mindId);
        member.Implant = implant;
        member.Job = job;

        if (!_roles.MindHasRole<InstituteAgentRoleComponent>(mindId))
        {
            _roles.MindAddRole(mindId, new InstituteAgentRoleComponent { PrototypeId = "InstituteAgent" }, mind, true);
        }

        // The tablet they spawned with uses the same PIN.
        if (mind.OwnedEntity is { } body &&
            _inventory.TryGetSlotEntity(body, "id", out var idItem) &&
            TryComp<InstituteTabletLockComponent>(idItem, out var tablet))
        {
            tablet.Owned = true;
            tablet.OwnerMind = mindId;
            tablet.Pin = uplink.Pin;
            tablet.UnlockedFor = null;
            Dirty(idItem.Value, tablet);
        }

        EnsureRule();
        Message(mindId, Loc.GetString("institute-uplink-welcome"));
    }

    public EntityUid? FindImplant(EntityUid body)
    {
        if (!TryComp<ImplantedComponent>(body, out var implanted))
            return null;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (HasComp<InstituteUplinkComponent>(implant))
                return implant;
        }
        return null;
    }

    private string NewPin()
    {
        return _random.Next(0, 10000).ToString("D4");
    }

    #endregion

    #region PIN lock

    /// <summary>
    /// The store window is registered here and not in YAML: this fork has two enums named StoreUiKey
    /// (the shop's and the NC trade machines'), and YAML's enum.StoreUiKey.Key picks the trade one.
    /// </summary>
    private void OnUplinkInit(Entity<InstituteUplinkComponent> ent, ref MapInitEvent args)
    {
        _ui.SetUi(ent.Owner, StoreUiKey.Key, new InterfaceData("StoreBoundUserInterface", 0f, false));
    }

    private void OnTabletLocked(Entity<InstituteTabletLockComponent> ent, ref InstituteTabletLockedEvent args)
    {
        if (TryComp<ActorComponent>(args.User, out var actor))
            RaiseNetworkEvent(new InstitutePinRequestEvent { Implant = GetNetEntity(ent), Tablet = true }, actor.PlayerSession);
    }

    private void OnOpenUplink(Entity<InstituteUplinkComponent> ent, ref OpenInstituteUplinkEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        // A refund rebuilds the store's listings from prototypes, so put the Director's stock back first.
        if (TryComp<StoreComponent>(ent, out var store))
            RefreshStock(ent, store);

        var user = args.Performer;
        if (ent.Comp.UnlockedFor == user)
        {
            _store.ToggleUi(user, ent);
            return;
        }

        if (TryComp<ActorComponent>(user, out var actor))
            RaiseNetworkEvent(new InstitutePinRequestEvent { Implant = GetNetEntity(ent) }, actor.PlayerSession);
    }

    private void OnPinSubmit(InstitutePinSubmitEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        var implant = GetEntity(msg.Implant);
        if (TryComp<InstituteTabletLockComponent>(implant, out var tablet))
        {
            SubmitTabletPin((implant, tablet), user, msg, args.SenderSession);
            return;
        }

        if (!TryComp<InstituteUplinkComponent>(implant, out var uplink) ||
            !TryComp<SubdermalImplantComponent>(implant, out var sub) ||
            sub.ImplantedEntity != user)
            return;

        if (_timing.CurTime < uplink.NextAttempt)
            return;
        uplink.NextAttempt = _timing.CurTime + PinCooldown;

        if (msg.Pin.Trim() == uplink.Pin)
        {
            uplink.UnlockedFor = user;
            _store.ToggleUi(user, implant);
            return;
        }

        RaiseNetworkEvent(new InstitutePinRequestEvent
        {
            Implant = msg.Implant,
            Error = Loc.GetString("institute-uplink-pin-wrong"),
        }, args.SenderSession);

        AlertDirectors(Loc.GetString("institute-uplink-pin-alert", ("name", Name(user))));
    }

    private void SubmitTabletPin(Entity<InstituteTabletLockComponent> tablet, EntityUid user, InstitutePinSubmitEvent msg, ICommonSession session)
    {
        // The tablet has to be on the person typing the PIN.
        if (!tablet.Comp.Owned || !IsHeldBy(tablet, user))
            return;
        if (_timing.CurTime < tablet.Comp.NextAttempt)
            return;
        tablet.Comp.NextAttempt = _timing.CurTime + PinCooldown;

        if (msg.Pin.Trim() == tablet.Comp.Pin)
        {
            _tabletLock.Unlock(tablet, user);
            _popup.PopupEntity(Loc.GetString("institute-tablet-unlocked"), user, user);
            return;
        }

        RaiseNetworkEvent(new InstitutePinRequestEvent
        {
            Implant = msg.Implant,
            Tablet = true,
            Error = Loc.GetString("institute-uplink-pin-wrong"),
        }, session);
        AlertDirectors(Loc.GetString("institute-tablet-pin-alert", ("name", Name(user))));
    }

    private bool IsHeldBy(EntityUid item, EntityUid user)
    {
        var parent = Transform(item).ParentUid;
        while (parent.IsValid())
        {
            if (parent == user)
                return true;
            parent = Transform(parent).ParentUid;
        }
        return false;
    }

    private void OnBriefing(Entity<InstituteMemberComponent> ent, ref GetBriefingEvent args)
    {
        args.Append(Loc.GetString("institute-uplink-briefing"));
        if (ent.Comp.Implant is { } implant &&
            TryComp<InstituteUplinkComponent>(implant, out var uplink) &&
            uplink.OwnerMind == ent.Owner)
        {
            args.Append(Loc.GetString("institute-uplink-briefing-pin", ("pin", uplink.Pin)));
        }
    }

    /// <summary>
    /// Gives the member's implant and tablets a new PIN (random if none is given).
    /// A shop or tablet that is already unlocked stays open.
    /// </summary>
    public void ResetPin(EntityUid mindId, string by, string? pin = null)
    {
        pin ??= NewPin();
        if (TryComp<InstituteMemberComponent>(mindId, out var member) &&
            member.Implant is { } implant &&
            TryComp<InstituteUplinkComponent>(implant, out var uplink))
        {
            uplink.Pin = pin;
        }

        var tablets = EntityQueryEnumerator<InstituteTabletLockComponent>();
        while (tablets.MoveNext(out _, out var tablet))
        {
            if (tablet.OwnerMind == mindId)
                tablet.Pin = pin;
        }

        Message(mindId, Loc.GetString("institute-uplink-pin-reset", ("by", by)));
    }

    public string? GetPin(EntityUid mindId)
    {
        return TryComp<InstituteMemberComponent>(mindId, out var member) &&
               member.Implant is { } implant &&
               TryComp<InstituteUplinkComponent>(implant, out var uplink)
            ? uplink.Pin
            : null;
    }

    public static bool ValidPin(string pin)
    {
        return pin.Length == 4 && pin.All(char.IsDigit);
    }

    #endregion

    #region Helpers

    private void OnBuyFinished(ref StoreBuyFinishedEvent args)
    {
        if (!HasComp<InstituteUplinkComponent>(args.StoreUid))
            return;

        // The free network listing is not a delivery, so no flash.
        if (args.PurchasedItem.ProductEvent is OpenInstituteNetworkEvent)
            return;

        Spawn(RelayIn, Transform(args.Buyer).Coordinates);
    }

    private void AlertDirectors(string text)
    {
        var query = EntityQueryEnumerator<InstituteMemberComponent>();
        while (query.MoveNext(out var mindId, out var member))
        {
            if (member.Job != Director)
                continue;

            Message(mindId, text, Color.OrangeRed);
            if (TryComp<MindComponent>(mindId, out var mind) && mind.OwnedEntity is { } body)
                _popup.PopupEntity(text, body, body);
        }
    }

    /// <summary>Private chat message to a mind's player, if they are connected.</summary>
    private void Message(EntityUid mindId, string text, Color? color = null)
    {
        if (!_minds.TryGetSession(mindId, out var session))
            return;

        var wrapped = Loc.GetString("institute-uplink-chat-wrap", ("message", FormattedMessage.EscapeText(text)));
        _chat.ChatMessageToOne(ChatChannel.Server, text, wrapped, default, false, session.Channel, color ?? InstituteColor);
    }

    private StoreComponent? MemberStore(EntityUid mindId, out EntityUid implant)
    {
        implant = default;
        if (!TryComp<InstituteMemberComponent>(mindId, out var member) ||
            member.Implant is not { } ent ||
            TerminatingOrDeleted(ent) ||
            !TryComp<StoreComponent>(ent, out var store))
            return null;

        implant = ent;
        return store;
    }

    private int Balance(StoreComponent store)
    {
        return store.Balance.TryGetValue(Currency, out var value) ? value.Int() : 0;
    }

    private void AddPoints(EntityUid implant, StoreComponent store, int amount)
    {
        if (amount == 0)
            return;

        store.Balance.TryAdd(Currency, FixedPoint2.Zero);
        store.Balance[Currency] += amount;
        _store.UpdateUserInterface(null, implant, store);
    }

    #endregion
}
