using Content.Server.Emp;
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
/// Changing a core takes three seconds while worn. Anyone servicing another player's suit warns the wearer.
/// </summary>
public sealed class FusionCorePoweredSystem : SharedFusionCorePoweredSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
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
        SubscribeLocalEvent<FusionCorePoweredComponent, FusionCoreChangeDoAfterEvent>(OnCoreChangeDoAfter);
        SubscribeLocalEvent<BatteryComponent, EmpAttemptEvent>(OnCoreEmpAttempt);
        SubscribeLocalEvent<FusionCorePoweredComponent, EmpAttemptEvent>((EntityUid uid, FusionCorePoweredComponent comp, EmpAttemptEvent ev) => ev.Cancel());
        SubscribeLocalEvent<ModularPowerArmorComponent, EmpAttemptEvent>((EntityUid uid, ModularPowerArmorComponent comp, EmpAttemptEvent ev) => ev.Cancel());

        SubscribeLocalEvent<InventoryComponent, GetVerbsEvent<AlternativeVerb>>(OnWearerVerbs);
        SubscribeLocalEvent<InventoryComponent, InteractUsingEvent>(OnWearerInteractUsing);

        SubscribeLocalEvent<ToggleableClothingComponent, GetVerbsEvent<AlternativeVerb>>(OnSuitVerbs);
        SubscribeLocalEvent<ToggleableClothingComponent, InteractUsingEvent>(OnSuitInteractUsing);
    }

    private void OnCoreEmpAttempt(EntityUid uid, BatteryComponent battery, EmpAttemptEvent args)
    {
        var parent = Transform(uid).ParentUid;
        if (TryComp<FusionCorePoweredComponent>(parent, out var rig) &&
            _itemSlots.GetItemOrNull(parent, rig.SlotId) == uid)
            args.Cancel();
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

    #region Worn suit (3 seconds)

    private void OnWearerVerbs(Entity<InventoryComponent> wearer, ref GetVerbsEvent<AlternativeVerb> args)
    {
        AddWornCoreVerbs(wearer, args);
    }

    private void AddWornCoreVerbs(EntityUid target, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null
            || GetWornPowerSystems(target) is not { } systems)
            return;

        var user = args.User;

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

        args.Handled = TryStartInstallInWearer(args.User, wearer, args.Used);
    }

    /// <summary>
    /// Starts the 3-second pull of the fusion core out of <paramref name="target"/>'s power armour,
    /// warning the wearer if another player is doing it.
    /// </summary>
    public bool TryStartRemoveFromWearer(EntityUid user, EntityUid target)
    {
        if (GetWornPowerSystems(target) is not { } systems
            || _itemSlots.GetItemOrNull(systems, systems.Comp.SlotId) is not { } previous)
            return false;

        if (!StartWearerDoAfter(user, target, systems, previous, null))
            return false;

        if (user == target)
            _popup.PopupEntity(Loc.GetString("fusion-core-start-removing-own"), user, user);
        else
        {
            _popup.PopupEntity(Loc.GetString("fusion-core-being-removed"), target, target, PopupType.LargeCaution);
            _popup.PopupEntity(Loc.GetString("fusion-core-start-removing", ("target", Identity.Entity(target, EntityManager))), user, user);
        }
        return true;
    }

    /// <summary>
    /// Starts the 3-second job of fitting <paramref name="core"/> into <paramref name="target"/>'s power armour.
    /// </summary>
    public bool TryStartInstallInWearer(EntityUid user, EntityUid target, EntityUid core)
    {
        if (!_hands.IsHolding(user, core) || !_tags.HasTag(core, FusionCoreTag) || GetWornPowerSystems(target) is not { } systems)
            return false;

        if (!StartWearerDoAfter(user, target, systems, _itemSlots.GetItemOrNull(systems, systems.Comp.SlotId), core))
            return false;

        if (user == target)
            _popup.PopupEntity(Loc.GetString("fusion-core-start-installing-own"), user, user);
        else
        {
            _popup.PopupEntity(Loc.GetString("fusion-core-being-installed"), target, target, PopupType.Medium);
            _popup.PopupEntity(Loc.GetString("fusion-core-start-installing", ("target", Identity.Entity(target, EntityManager))), user, user);
        }
        return true;
    }

    private bool StartWearerDoAfter(EntityUid user, EntityUid target, Entity<FusionCorePoweredComponent> systems,
        EntityUid? previous, EntityUid? used)
    {
        var ev = new FusionCoreChangeDoAfterEvent { PreviousCore = GetNetEntity(previous) };
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

    private void OnCoreChangeDoAfter(Entity<FusionCorePoweredComponent> ent, ref FusionCoreChangeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } wearer || GetWornPowerSystems(wearer)?.Owner != ent.Owner
            || GetNetEntity(_itemSlots.GetItemOrNull(ent, ent.Comp.SlotId)) != args.PreviousCore)
            return;
        args.Handled = true;

        if (args.Used is { } replacement)
        {
            if (!_hands.IsHolding(args.User, replacement) || !ReplaceCore(ent, replacement, args.User))
                return;
            _popup.PopupEntity(Loc.GetString("fusion-core-installed-in-you"), wearer, wearer, PopupType.Medium);
        }
        else if (EjectCore(ent, args.User) is { } removed)
        {
            _hands.PickupOrDrop(args.User, removed);
            if (args.User != wearer)
                _popup.PopupEntity(Loc.GetString("fusion-core-removed-from-you"), wearer, wearer, PopupType.LargeCaution);
        }
        UpdatePowered(ent);
    }

    #endregion

    #region Interacting with the suit

    private void OnSuitVerbs(Entity<ToggleableClothingComponent> suit, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (Inventory.TryGetSlotEntity(args.User, "outerClothing", out var worn) && worn == suit.Owner)
        {
            AddWornCoreVerbs(args.User, args);
            return;
        }

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

        args.Handled = Inventory.TryGetSlotEntity(args.User, "outerClothing", out var worn) && worn == suit.Owner
            ? TryStartInstallInWearer(args.User, args.User, args.Used)
            : TryInsertCoreIntoSuit(suit, args.Used, args.User);
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

        var inserted = ReplaceCore(systems, core, user);
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
        var ejected = _itemSlots.TryEject(systems, systems.Comp.SlotId, user, out var core);
        _itemSlots.SetLock(systems, systems.Comp.SlotId, true);
        return ejected ? core : null;
    }

    private bool ReplaceCore(Entity<FusionCorePoweredComponent> systems, EntityUid core, EntityUid user)
    {
        var id = systems.Comp.SlotId;
        if (!_itemSlots.TryGetSlot(systems, id, out var slot))
            return false;
        _itemSlots.SetLock(systems, id, false);
        try
        {
            if (!_itemSlots.CanInsert(systems, core, user, slot, swap: true))
                return false;
            var old = slot.Item;
            if (old != null && !_itemSlots.TryEject(systems, slot, user, out _))
                return false;
            var inserted = _itemSlots.TryInsert(systems, slot, core, user) && slot.Item == core;
            if (old is { } previous && (inserted || !_itemSlots.TryInsert(systems, slot, previous, user)))
                _hands.PickupOrDrop(user, previous);
            return inserted;
        }
        finally
        {
            _itemSlots.SetLock(systems, id, true);
        }
    }

    /// <summary>
    /// The power systems of the power armour someone is wearing: the deployed back piece, or the
    /// folded-away one inside their worn suit.
    /// </summary>
    private Entity<FusionCorePoweredComponent>? GetWornPowerSystems(EntityUid wearer)
    {
        if (Inventory.TryGetSlotEntity(wearer, "back", out var back)
            && TryComp<FusionCorePoweredComponent>(back, out var deployed))
            return (back.Value, deployed);

        if (Inventory.TryGetSlotEntity(wearer, "outerClothing", out var suit))
            return GetSuitPowerSystems(suit.Value);

        return null;
    }

    private bool IsWorn(EntityUid item)
    {
        return _container.TryGetContainingContainer((item, null, null), out var container)
            && Inventory.TryGetSlotEntity(container.Owner, "outerClothing", out var suit) && suit == item;
    }

    #endregion
}
