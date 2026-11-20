using System.Linq;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;

namespace Content.Server._ReclamationProject.PowerArmor;

public sealed partial class PowerArmorModSystem : EntitySystem
{
    private const string Sockets = "armor-mods";
    private const string Holster = "wrist-tool";
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ToggleableClothingSystem _attachments = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly FusionCorePoweredSystem _power = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    private readonly HashSet<EntityUid> _refresh = new();

    public override void Initialize()
    {
        Type[] beforeRepair = [typeof(Content.Server.Repairable.RepairableSystem)];
        SubscribeLocalEvent<PowerArmorPlateComponent, InteractUsingEvent>(OnFit, before: beforeRepair);
        SubscribeLocalEvent<PowerArmorPlateComponent, BuildArmorModEvent>(FinishStep);
        SubscribeLocalEvent<ModularPowerArmorComponent, InteractUsingEvent>(OnFitToSuit, before: beforeRepair);
        SubscribeLocalEvent<AttachedClothingComponent, InteractUsingEvent>(OnServiceGlove, before: beforeRepair);
        SubscribeLocalEvent<PowerArmorPlateComponent, GetVerbsEvent<AlternativeVerb>>(OnServiceVerbs);
        SubscribeLocalEvent<PowerArmorModWearerComponent, DidEquipEvent>((uid, _, _) => _refresh.Add(uid));
        SubscribeLocalEvent<PowerArmorModWearerComponent, DidUnequipEvent>((uid, _, _) => _refresh.Add(uid));
        SubscribeLocalEvent<PowerArmorModWearerComponent, EntityTerminatingEvent>(OnWearerTerminating);
        InitializeAbilities();
    }

    public override void Update(float frameTime)
    {
        if (_refresh.Count == 0) return;
        var pending = _refresh.ToArray();
        _refresh.Clear();
        foreach (var user in pending)
            if (!TerminatingOrDeleted(user)) RefreshWearer(user);
    }

    private IReadOnlyList<EntityUid> Installed(EntityUid plate) =>
        _containers.TryGetContainer(plate, Sockets, out var sockets) ? sockets.ContainedEntities : Array.Empty<EntityUid>();

    public IEnumerable<EntityUid> Fitted(EntityUid frame)
    {
        foreach (var slot in PowerArmorPlates.Slots)
            if (_slots.GetItemOrNull(frame, PowerArmorPlates.Container(slot)) is {} plate)
                foreach (var mod in Installed(plate))
                    if (HasComp<PowerArmorModComponent>(mod)) yield return mod;
    }

    public bool AcceptsPlate(Entity<ModularPowerArmorComponent> frame, EntityUid plate)
    {
        var incoming = Installed(plate);
        return incoming.Count == 0 || Fitted(frame).Count() + incoming.Count <= frame.Comp.ModCapacity;
    }

    private bool IsMechanic(EntityUid user) => _factions.IsMember(user, "AtomCats");

    private void OnWearerTerminating(Entity<PowerArmorModWearerComponent> ent, ref EntityTerminatingEvent ev) =>
        ClearWearer(ent, ent.Comp);

    private void OnFit(EntityUid plate, PowerArmorPlateComponent comp, InteractUsingEvent ev)
    {
        if (ev.Handled) return;
        if (!TryComp<PowerArmorModComponent>(ev.Used, out var mod))
        {
            if (comp.Slot == ArmorPlateSlot.Gloves && ServiceWrist(plate, ev)) return;
            BuildInteraction((plate, comp), ev);
            return;
        }
        ev.Handled = true;
        Fit(ev.User, (plate, comp), (ev.Used, mod));
    }

    private void OnFitToSuit(EntityUid frame, ModularPowerArmorComponent comp, InteractUsingEvent ev)
    {
        if (ev.Handled) return;
        if (!TryComp<PowerArmorModComponent>(ev.Used, out var mod))
        {
            if (_slots.GetItemOrNull(frame, "plate-gloves") is {} gloves) ServiceWrist(gloves, ev);
            return;
        }
        ev.Handled = true;
        if (_slots.GetItemOrNull(frame, PowerArmorPlates.Container(mod.Slot)) is {} plate)
            Fit(ev.User, (plate, Comp<PowerArmorPlateComponent>(plate)), (ev.Used, mod));
        else Notice(ev.User, "pa-mod-missing-plate");
    }

    private void Fit(EntityUid user, Entity<PowerArmorPlateComponent> plate, Entity<PowerArmorModComponent> mod)
    {
        if (!IsMechanic(user)) { Notice(user, "pa-mod-mechanic-only"); return; }
        if (!_hands.IsHolding(user, mod)) return;
        if (plate.Comp.Project != null) { Notice(user, "pa-mod-busy"); return; }
        if (mod.Comp.Slot != plate.Comp.Slot) { Notice(user, "pa-mod-wrong-plate"); return; }
        InstallMod(user, plate, mod.Owner);
    }

    private void OnServiceGlove(EntityUid uid, AttachedClothingComponent attached, InteractUsingEvent ev)
    {
        var frame = attached.AttachedUid;
        if (ev.Handled || !HasComp<ModularPowerArmorComponent>(frame) ||
            !_inventory.TryGetSlotEntity(ev.User, "gloves", out var gloves) || gloves != uid ||
            _slots.GetItemOrNull(frame, "plate-gloves") is not {} plate) return;
        ServiceWrist(plate, ev);
    }

    private bool InstallMod(EntityUid user, EntityUid plate, EntityUid module)
    {
        var sockets = _containers.EnsureContainer<Container>(plate, Sockets);
        var previous = sockets.ContainedEntities.ToArray();
        var frame = Transform(plate).ParentUid;
        if (TryComp<ModularPowerArmorComponent>(frame, out var shell) &&
            Fitted(frame).Count() - previous.Length >= shell.ModCapacity)
        {
            Notice(user, "pa-mod-full");
            return false;
        }
        if (!_containers.Insert(module, sockets))
            return false;

        foreach (var old in previous)
        {
            if (TryComp<PowerArmorModComponent>(old, out var data) && data.Wearer != null)
                Deactivate(old, data);
            Del(old);
        }
        UpdatePlateMod(plate);
        if (HasComp<ModularPowerArmorComponent>(frame)) FrameChanged(frame);
        var message = previous.Length == 0 ? "pa-mod-installed" : "pa-mod-replaced";
        _popup.PopupEntity(Loc.GetString(message, ("mod", module), ("plate", plate)), user, user);
        return true;
    }

    private void OnServiceVerbs(EntityUid plate, PowerArmorPlateComponent comp, GetVerbsEvent<AlternativeVerb> ev)
    {
        if (!ev.CanAccess || !ev.CanInteract || ev.Hands == null || !IsMechanic(ev.User)) return;
        AddBuildVerbs((plate, comp), ev);
        foreach (var mod in Installed(plate))
        {
            var user = ev.User;
            ev.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("pa-mod-remove", ("mod", mod)),
                Act = () => {
                    if (!IsMechanic(user) || !_containers.TryGetContainer(plate, Sockets, out var sockets) || !sockets.Contains(mod)) return;
                    var frame = Transform(plate).ParentUid;
                    if (!_containers.Remove(mod, sockets, destination: Transform(user).Coordinates)) return;
                    UpdatePlateMod(plate);
                    if (HasComp<ModularPowerArmorComponent>(frame)) FrameChanged(frame);
                    _hands.TryPickupAnyHand(user, mod);
                }
            });
        }
    }

    private void Notice(EntityUid user, string key) => _popup.PopupEntity(Loc.GetString(key), user, user);

    private void UpdatePlateMod(EntityUid plate)
    {
        var part = Comp<PowerArmorPlateComponent>(plate);
        var fitted = Installed(plate);
        var kind = fitted.Count == 0 ? (ArmorModKind?) null : Comp<PowerArmorModComponent>(fitted[0]).Kind;
        if (part.FittedMod == kind) return;
        part.FittedMod = kind;
        Dirty(plate, part);
    }

    public void FrameChanged(EntityUid frame)
    {
        var shell = Comp<ModularPowerArmorComponent>(frame);
        var mods = Fitted(frame).Select(uid => Comp<PowerArmorModComponent>(uid)).ToArray();
        shell.ModVisuals.Clear();
        foreach (var mod in mods)
            shell.ModVisuals[mod.Slot] = mod.Kind;
        Dirty(frame, shell);
        var overdrive = mods.Any(m => m.Kind == ArmorModKind.Overdrive);
        if (overdrive != shell.Overdrive) { shell.Overdrive = overdrive; Dirty(frame, shell); }
        var parent = Transform(frame).ParentUid;
        EntityUid? wearer = HasComp<InventoryComponent>(parent) && _inventory.TryGetSlotEntity(parent, "outerClothing", out var outer) && outer == frame ? parent : null;
        if (TryComp<ToggleableClothingComponent>(frame, out var kit))
        {
            var melee = mods.FirstOrDefault(m => m.Kind == ArmorModKind.Knuckles ||
                m.Kind == ArmorModKind.Deathclaw && m.SwitchedOn);
            var gloves = melee?.Kind switch
            {
                ArmorModKind.Knuckles => "AtomCatsKnuckleGauntlets",
                ArmorModKind.Deathclaw => "AtomCatsClawGauntlets",
                _ => "ReclamationFrameGauntlets"
            };
            _attachments.SetAttachmentPrototype((frame, kit), "gloves", gloves, wearer);
        }
        if (wearer is not {} user || mods.Length == 0 && !HasComp<PowerArmorModWearerComponent>(user)) return;
        EnsureComp<PowerArmorModWearerComponent>(user);
        RefreshWearer(user);
        _refresh.Add(user); // Attached clothes may finish equipping later in the same tick.
        _movement.RefreshMovementSpeedModifiers(user);
    }

    public void ClearWearer(EntityUid user)
    {
        if (TryComp<PowerArmorModWearerComponent>(user, out var state)) ClearWearer(user, state);
    }

    private void ClearWearer(EntityUid user, PowerArmorModWearerComponent state)
    {
        foreach (var mod in state.Modules)
            if (TryComp<PowerArmorModComponent>(mod, out var data) && data.Wearer == user) Deactivate(mod, data);
        state.Modules.Clear();
        SetVisors(user, state, false, false);
    }

    private void Deactivate(EntityUid uid, PowerArmorModComponent mod)
    {
        Stow(uid, mod);
        _actions.RemoveAction(mod.ActionEntity);
        mod.Wearer = null;
        if (mod.Kind == ArmorModKind.NightVision)
        {
            mod.SwitchedOn = false;
            _actions.SetToggled(mod.ActionEntity, false);
        }
    }

    private bool InUse(EntityUid frame, EntityUid user, PowerArmorModComponent mod)
    {
        var slot = mod.Slot switch { ArmorPlateSlot.Gloves => "gloves", ArmorPlateSlot.Helmet => "head", _ => null };
        return slot == null || _inventory.TryGetSlotEntity(user, slot, out var item) &&
            TryComp<AttachedClothingComponent>(item, out var gear) && gear.AttachedUid == frame;
    }

    public void RefreshWearer(EntityUid user)
    {
        if (!TryComp<PowerArmorModWearerComponent>(user, out var state)) return;
        if (!_inventory.TryGetSlotEntity(user, "outerClothing", out var outer) || !HasComp<ModularPowerArmorComponent>(outer))
        { ClearWearer(user, state); return; }
        var frame = outer.Value;
        var active = Fitted(frame).Where(m => InUse(frame, user, Comp<PowerArmorModComponent>(m))).ToHashSet();
        foreach (var old in state.Modules.Except(active))
            if (TryComp<PowerArmorModComponent>(old, out var gone) && gone.Wearer == user) Deactivate(old, gone);
        var night = false;
        var medical = false;
        foreach (var uid in active)
        {
            var mod = Comp<PowerArmorModComponent>(uid);
            if (mod.Wearer != user || !state.Modules.Contains(uid))
            {
                if (mod.Wearer != null) Deactivate(uid, mod);
                mod.Wearer = user;
                if (mod.Action is {} action) _actions.AddAction(user, ref mod.ActionEntity, action, uid);
            }
            night |= mod.Kind == ArmorModKind.NightVision && mod.SwitchedOn;
            medical |= mod.Kind == ArmorModKind.MedicalHud;
        }
        state.Modules = active;
        SetVisors(user, state, medical, night);
    }

    private void SetVisors(EntityUid user, PowerArmorModWearerComponent state, bool medical, bool night)
    {
        if (state.MedicalHud == medical && state.NightVision == night) return;
        state.MedicalHud = medical;
        state.NightVision = night;
        Dirty(user, state);
    }
}
