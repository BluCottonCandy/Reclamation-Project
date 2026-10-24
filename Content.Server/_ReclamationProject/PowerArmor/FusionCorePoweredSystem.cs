using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.PowerArmor;

/// <summary>
/// Power armour fusion cores. The wearer switches the suit on and off from inside; while on and
/// charged it drains the core and moves at normal power armour speed, otherwise it crawls.
/// Cores only go in or out from outside the suit: instantly while nobody wears it, or in 3 seconds
/// by someone else while it's worn (pulling one out warns the wearer).
/// </summary>
public sealed class FusionCorePoweredSystem : SharedFusionCorePoweredSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _tags = default!;

    private const string FusionCoreTag = "N14FusionCore";
    private static readonly TimeSpan DrainInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FusionCorePoweredComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FusionCorePoweredComponent, FusionCorePowerToggleEvent>(OnPowerToggle);
        SubscribeLocalEvent<FusionCorePoweredComponent, FusionCoreRemoveDoAfterEvent>(OnRemoveDoAfter);
        SubscribeLocalEvent<FusionCorePoweredComponent, FusionCoreInstallDoAfterEvent>(OnInstallDoAfter);

        // Another player working on someone's worn suit.
        SubscribeLocalEvent<InventoryComponent, GetVerbsEvent<AlternativeVerb>>(OnWearerVerbs);
        SubscribeLocalEvent<InventoryComponent, InteractUsingEvent>(OnWearerInteractUsing);

        // The suit itself while nobody wears it (on the floor, in a hand).
        SubscribeLocalEvent<ToggleableClothingComponent, GetVerbsEvent<AlternativeVerb>>(OnSuitVerbs);
        SubscribeLocalEvent<ToggleableClothingComponent, InteractUsingEvent>(OnSuitInteractUsing);
    }

    private void OnMapInit(Entity<FusionCorePoweredComponent> ent, ref MapInitEvent args)
    {
        // Normal item-slot verbs can't touch the core; every way in or out goes through this system.
        _itemSlots.SetLock(ent, ent.Comp.SlotId, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FusionCorePoweredComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextDrain)
                continue;
            comp.NextDrain = now + DrainInterval;

            var wearer = GetWearer(uid);
            var core = _itemSlots.GetItemOrNull(uid, comp.SlotId);

            if (wearer != null && comp.SwitchedOn && TryComp<BatteryComponent>(core, out var battery) && battery.CurrentCharge > 0)
                _battery.UseCharge(core.Value, comp.DrainPerSecond * (float) DrainInterval.TotalSeconds, battery);

            UpdatePowered((uid, comp), wearer);
        }
    }

    /// <summary>
    /// Re-checks the switch and the core's charge and updates speed. Public so tests and other systems can force it.
    /// </summary>
    public void UpdatePowered(Entity<FusionCorePoweredComponent> ent, EntityUid? wearer = null)
    {
        wearer ??= GetWearer(ent);
        var hasCharge = HasCharge(ent);
        var powered = ent.Comp.SwitchedOn && hasCharge;

        if (powered == ent.Comp.Powered)
            return;

        ent.Comp.Powered = powered;
        Dirty(ent);

        if (wearer == null)
            return;

        Movement.RefreshMovementSpeedModifiers(wearer.Value);

        // The on/off switch has its own popup; these are for the core itself.
        if (!ent.Comp.SwitchedOn)
            return;

        _popup.PopupEntity(Loc.GetString(powered ? "fusion-core-powered" : "fusion-core-depleted"), wearer.Value, wearer.Value,
            powered ? PopupType.Medium : PopupType.MediumCaution);
    }

    private bool HasCharge(Entity<FusionCorePoweredComponent> ent)
    {
        var core = _itemSlots.GetItemOrNull(ent, ent.Comp.SlotId);
        return TryComp<BatteryComponent>(core, out var battery) && battery.CurrentCharge > 0;
    }

    #region Wearer's power switch

    private void OnPowerToggle(Entity<FusionCorePoweredComponent> ent, ref FusionCorePowerToggleEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        ent.Comp.SwitchedOn = !ent.Comp.SwitchedOn;
        Actions.SetToggled(ent.Comp.PowerActionEntity, ent.Comp.SwitchedOn);
        Dirty(ent);

        _popup.PopupEntity(Loc.GetString(ent.Comp.SwitchedOn ? "fusion-core-switched-on" : "fusion-core-switched-off"),
            args.Performer, args.Performer);
        UpdatePowered(ent, args.Performer);
    }

    #endregion

    #region Someone else working on a worn suit (3 seconds)

    private void OnWearerVerbs(Entity<InventoryComponent> wearer, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null
            || args.User == args.Target // can't reach your own core from inside
            || GetWornPowerSystems(wearer) is not { } systems)
            return;

        var user = args.User;
        var target = wearer.Owner;

        if (_itemSlots.GetItemOrNull(systems, systems.Comp.SlotId) != null)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("fusion-core-verb-remove"),
                Act = () => TryStartRemoveFromWearer(user, target),
            });
        }

        if (_hands.GetActiveItem(user) is { } held && _tags.HasTag(held, FusionCoreTag))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("fusion-core-verb-install"),
                Act = () => TryStartInstallInWearer(user, target, held),
            });
        }
    }

    private void OnWearerInteractUsing(Entity<InventoryComponent> wearer, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tags.HasTag(args.Used, FusionCoreTag) || GetWornPowerSystems(wearer) == null)
            return;

        if (args.User == args.Target)
        {
            _popup.PopupEntity(Loc.GetString("fusion-core-cant-reach-own"), args.User, args.User);
            args.Handled = true;
            return;
        }

        args.Handled = TryStartInstallInWearer(args.User, wearer, args.Used);
    }

    /// <summary>
    /// Starts the 3-second pull of the fusion core out of <paramref name="target"/>'s power armour,
    /// warning them loudly.
    /// </summary>
    public bool TryStartRemoveFromWearer(EntityUid user, EntityUid target)
    {
        if (user == target || GetWornPowerSystems(target) is not { } systems
            || _itemSlots.GetItemOrNull(systems, systems.Comp.SlotId) == null)
            return false;

        if (!StartWearerDoAfter(user, target, systems, new FusionCoreRemoveDoAfterEvent(), null))
            return false;

        _popup.PopupEntity(Loc.GetString("fusion-core-being-removed"), target, target, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("fusion-core-start-removing", ("target", Identity.Entity(target, EntityManager))),
            user, user);
        return true;
    }

    /// <summary>
    /// Starts the 3-second job of fitting <paramref name="core"/> into <paramref name="target"/>'s power armour.
    /// </summary>
    public bool TryStartInstallInWearer(EntityUid user, EntityUid target, EntityUid core)
    {
        if (user == target || !_tags.HasTag(core, FusionCoreTag) || GetWornPowerSystems(target) is not { } systems)
            return false;

        if (!StartWearerDoAfter(user, target, systems, new FusionCoreInstallDoAfterEvent(), core))
            return false;

        _popup.PopupEntity(Loc.GetString("fusion-core-being-installed"), target, target, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("fusion-core-start-installing", ("target", Identity.Entity(target, EntityManager))),
            user, user);
        return true;
    }

    private bool StartWearerDoAfter(EntityUid user, EntityUid target, Entity<FusionCorePoweredComponent> systems,
        DoAfterEvent ev, EntityUid? used)
    {
        var doAfter = new DoAfterArgs(EntityManager, user, systems.Comp.WearerCoreDelay, ev, systems, target: target, used: used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnHandChange = true,
            DistanceThreshold = 1.5f,
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnRemoveDoAfter(Entity<FusionCorePoweredComponent> ent, ref FusionCoreRemoveDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;
        args.Handled = true;

        if (EjectCore(ent, args.User) is not { } core)
            return;

        _hands.PickupOrDrop(args.User, core);
        if (args.Target is { } target)
            _popup.PopupEntity(Loc.GetString("fusion-core-removed-from-you"), target, target, PopupType.LargeCaution);

        UpdatePowered(ent);
    }

    private void OnInstallDoAfter(Entity<FusionCorePoweredComponent> ent, ref FusionCoreInstallDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } core)
            return;
        args.Handled = true;

        var old = EjectCore(ent, args.User);
        InsertCore(ent, core, args.User);
        if (old != null)
            _hands.PickupOrDrop(args.User, old.Value);

        if (args.Target is { } target)
            _popup.PopupEntity(Loc.GetString("fusion-core-installed-in-you"), target, target, PopupType.Medium);

        UpdatePowered(ent);
    }

    #endregion

    #region Suit on the floor or in a hand (instant)

    private void OnSuitVerbs(Entity<ToggleableClothingComponent> suit, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null
            || GetSuitPowerSystems(suit) is not { } systems
            || IsWorn(suit) || _itemSlots.GetItemOrNull(systems, systems.Comp.SlotId) == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("fusion-core-verb-remove"),
            Act = () => TryRemoveCoreFromSuit(suit, user),
        });
    }

    private void OnSuitInteractUsing(Entity<ToggleableClothingComponent> suit, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tags.HasTag(args.Used, FusionCoreTag) || GetSuitPowerSystems(suit) == null)
            return;

        args.Handled = TryInsertCoreIntoSuit(suit, args.Used, args.User);
    }

    /// <summary>
    /// Pulls the fusion core out of a power armour suit nobody is wearing, into the user's hand.
    /// </summary>
    public bool TryRemoveCoreFromSuit(EntityUid suit, EntityUid user)
    {
        if (GetSuitPowerSystems(suit) is not { } systems || IsWorn(suit))
            return false;

        if (EjectCore(systems, user) is not { } core)
            return false;

        _hands.PickupOrDrop(user, core);
        UpdatePowered(systems);
        return true;
    }

    /// <summary>
    /// Puts a fusion core into a power armour suit nobody is wearing; any old core goes to the user's hand.
    /// </summary>
    public bool TryInsertCoreIntoSuit(EntityUid suit, EntityUid core, EntityUid user)
    {
        if (GetSuitPowerSystems(suit) is not { } systems || IsWorn(suit))
            return false;

        var old = EjectCore(systems, user);
        var inserted = InsertCore(systems, core, user);
        if (old != null)
            _hands.PickupOrDrop(user, old.Value);

        UpdatePowered(systems);
        return inserted;
    }

    #endregion

    #region Helpers

    private EntityUid? EjectCore(Entity<FusionCorePoweredComponent> systems, EntityUid user)
    {
        if (_itemSlots.GetItemOrNull(systems, systems.Comp.SlotId) == null)
            return null;

        _itemSlots.SetLock(systems, systems.Comp.SlotId, false);
        _itemSlots.TryEject(systems, systems.Comp.SlotId, user, out var core);
        _itemSlots.SetLock(systems, systems.Comp.SlotId, true);
        return core;
    }

    private bool InsertCore(Entity<FusionCorePoweredComponent> systems, EntityUid core, EntityUid user)
    {
        _itemSlots.SetLock(systems, systems.Comp.SlotId, false);
        var inserted = _itemSlots.TryInsert(systems, systems.Comp.SlotId, core, user);
        _itemSlots.SetLock(systems, systems.Comp.SlotId, true);
        return inserted;
    }

    /// <summary>
    /// The power systems of the power armour someone is wearing: the deployed back piece, or the
    /// folded-away one inside their worn suit.
    /// </summary>
    private Entity<FusionCorePoweredComponent>? GetWornPowerSystems(EntityUid wearer)
    {
        if (_inventory.TryGetSlotEntity(wearer, "back", out var back)
            && TryComp<FusionCorePoweredComponent>(back, out var deployed))
            return (back.Value, deployed);

        if (_inventory.TryGetSlotEntity(wearer, "outerClothing", out var suit))
            return GetSuitPowerSystems(suit.Value);

        return null;
    }

    private Entity<FusionCorePoweredComponent>? GetSuitPowerSystems(EntityUid suit)
    {
        if (!TryComp<ToggleableClothingComponent>(suit, out var toggleable))
            return null;

        foreach (var part in toggleable.ClothingUids.Keys)
        {
            if (TryComp<FusionCorePoweredComponent>(part, out var powered))
                return (part, powered);
        }

        return null;
    }

    private bool IsWorn(EntityUid item)
    {
        return _container.TryGetContainingContainer((item, null, null), out var container)
            && HasComp<InventoryComponent>(container.Owner);
    }

    private EntityUid? GetWearer(EntityUid piece)
    {
        if (!_container.TryGetContainingContainer((piece, null, null), out var container)
            || !HasComp<InventoryComponent>(container.Owner))
            return null;

        return container.Owner;
    }

    #endregion
}
