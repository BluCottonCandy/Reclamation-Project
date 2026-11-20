using System.Linq;
using Content.Server._Misfits.WastelandMap;
using Content.Shared._Misfits.WastelandMap;
using Content.Server.Power.Components;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Tools.Components;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._ReclamationProject.PowerArmor;

public sealed partial class PowerArmorModSystem
{
    [Dependency] private readonly ThrowingSystem _throw = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedGunSystem _guns = default!;
    [Dependency] private readonly WastelandMapSystem _maps = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    private const string WristMount = "power-armor-wrist";

    private void InitializeAbilities()
    {
        SubscribeLocalEvent<PowerArmorModComponent, MapInitEvent>(OnModInit);
        SubscribeLocalEvent<PowerArmorModComponent, ActivateArmorModEvent>(OnActivate);
        SubscribeLocalEvent<ArmorCasualtyDisplayComponent, BoundUIClosedEvent>((_, display, ev) =>
        {
            if (Equals(ev.UiKey, WastelandMapUiKey.Key) && TryComp<PowerArmorModComponent>(display.Module, out var mod))
                _actions.SetToggled(mod.ActionEntity, false);
        });
        SubscribeLocalEvent<ArmorCasualtyDisplayComponent, BoundUserInterfaceMessageAttempt>(
            (Entity<ArmorCasualtyDisplayComponent> ent, ref BoundUserInterfaceMessageAttempt ev) =>
            {
                if (!TryComp<PowerArmorModComponent>(ent.Comp.Module, out var mod) || mod.Wearer != ev.Actor)
                    ev.Cancel();
            });
        SubscribeLocalEvent<PowerArmorModComponent, EntityTerminatingEvent>(OnModuleTerminating);
        SubscribeLocalEvent<PowerArmorWristToolComponent, ContainerGettingRemovedAttemptEvent>((_, comp, ev) => {
            if (!comp.Moving) ev.Cancel();
        });
        SubscribeLocalEvent<PowerArmorModWearerComponent, BeforeInteractHandEvent>(OnWristClick);
        SubscribeLocalEvent<PowerArmorWornComponent, ExaminedEvent>((_, worn, ev) => {
            if (ev.IsInDetailsRange && HasComp<ModularPowerArmorComponent>(worn.Armor)) Scan(worn.Armor, ev);
        });
    }

    private void OnModuleTerminating(Entity<PowerArmorModComponent> ent, ref EntityTerminatingEvent ev)
    {
        ReleaseGarageMod(ent.Comp);
        RemoveCasualtyDisplay(ent.Comp);
        if (ent.Comp.ToolEntity is {} tool && !TerminatingOrDeleted(tool)) QueueDel(tool);
    }

    private void OnModInit(EntityUid uid, PowerArmorModComponent comp, MapInitEvent ev)
    {
        if (comp.Tool is not {} proto || comp.ToolEntity != null) return;
        var holster = _containers.EnsureContainer<ContainerSlot>(uid, Holster);
        var tool = Spawn(proto, Transform(uid).Coordinates);
        _containers.Insert(tool, holster);
        comp.ToolEntity = tool;
        EnsureComp<PowerArmorWristToolComponent>(tool).Module = uid;
    }

    private void OnActivate(EntityUid uid, PowerArmorModComponent comp, ActivateArmorModEvent ev)
    {
        var user = ev.Performer;
        if (ev.Handled || !WornModule((uid, comp), user, out var frame)) return;
        if (comp.ToolEntity is {} tool)
        {
            if (!TryComp<PowerArmorWristToolComponent>(tool, out var tether)) return;
            var wearer = Comp<PowerArmorModWearerComponent>(user);
            if (wearer.ActiveWristTool == tool) Stow(uid, comp);
            else
            {
                tether.Moving = true;
                try { if (!_containers.Insert(tool, _containers.EnsureContainer<ContainerSlot>(user, WristMount))) return; }
                finally { tether.Moving = false; }
                if (comp.Kind == ArmorModKind.Welder && !_toggle.TryActivate(tool, user))
                {
                    Stow(uid, comp);
                    return;
                }
                wearer.ActiveWristTool = tool;
                Dirty(user, wearer);
                _actions.SetToggled(comp.ActionEntity, true);
            }
        }
        else switch (comp.Kind)
        {
            case ArmorModKind.MagLock:
                ToggleMagLock(user, comp);
                break;
            case ArmorModKind.Junkhound:
                if (!PulseJunkhound(user)) return;
                break;
            case ArmorModKind.MedicalHud:
                ToggleCasualtyDisplay(uid, comp, user);
                break;
            case ArmorModKind.Jump:
                if (_power.GetSuitPowerSystems(frame) is not { Comp.Powered: true } || !_blocker.CanMove(user))
                { Notice(user, "pa-mod-no-jump"); return; }
                var start = _xform.GetMapCoordinates(user);
                var dir = _xform.GetWorldRotation(user).ToWorldVec();
                var end = new MapCoordinates(start.Position + dir * 5f, start.MapId);
                var distance = _interaction.UnobstructedDistance(start, end, (int) CollisionGroup.HighImpassable, e => e == user);
                if (distance < 5f) distance -= 0.4f;
                if (distance <= 0.1f) return;
                _throw.TryThrow(user, dir * distance, 10f, user, 0f, compensateFriction: true, recoil: false, doSpin: false);
                break;
            case ArmorModKind.NightVision:
                comp.SwitchedOn = !comp.SwitchedOn;
                _actions.SetToggled(comp.ActionEntity, comp.SwitchedOn);
                RefreshWearer(user);
                break;
            default: return;
        }
        ev.Handled = true;
    }

    private void ToggleCasualtyDisplay(EntityUid module, PowerArmorModComponent mod, EntityUid user)
    {
        // A worn display is directly accessible to its wearer while the plate socket remains closed.
        if (mod.CasualtyDisplay is not {} display || TerminatingOrDeleted(display))
        {
            display = Spawn("AtomCatsCasualtyDisplay", Transform(user).Coordinates);
            Comp<ArmorCasualtyDisplayComponent>(display).Module = module;
            if (!_containers.Insert(display, _containers.EnsureContainer<Container>(user, "armor-casualty-display")))
            {
                QueueDel(display);
                return;
            }
            mod.CasualtyDisplay = display;
        }
        if (_ui.IsUiOpen(display, WastelandMapUiKey.Key, user))
            _ui.CloseUi(display, WastelandMapUiKey.Key, user);
        else if (_ui.TryOpenUi(display, WastelandMapUiKey.Key, user))
        {
            _maps.RefreshUi(display, user);
            _actions.SetToggled(mod.ActionEntity, true);
        }
    }

    private void RemoveCasualtyDisplay(PowerArmorModComponent mod)
    {
        if (mod.CasualtyDisplay is {} display && !TerminatingOrDeleted(display))
        {
            _ui.CloseUi(display, WastelandMapUiKey.Key);
            QueueDel(display);
        }
        mod.CasualtyDisplay = null;
    }

    private void Stow(EntityUid uid, PowerArmorModComponent comp)
    {
        if (comp.ToolEntity is not {} tool || !TryComp<PowerArmorWristToolComponent>(tool, out var tether)
            || TerminatingOrDeleted(uid) || Transform(tool).ParentUid == uid) return;
        if (comp.Kind == ArmorModKind.Welder) _toggle.TryDeactivate(tool);
        if (TryComp<GunComponent>(tool, out var gun)) _guns.StopShooting(tool, gun);
        if (comp.Wearer is {} user && TryComp<PowerArmorModWearerComponent>(user, out var state) &&
            state.ActiveWristTool == tool)
        {
            state.ActiveWristTool = null;
            Dirty(user, state);
        }
        tether.Moving = true;
        try { _containers.Insert(tool, _containers.EnsureContainer<ContainerSlot>(uid, Holster)); }
        finally { tether.Moving = false; }
        _actions.SetToggled(comp.ActionEntity, false);
    }

    private void OnWristClick(EntityUid user, PowerArmorModWearerComponent state, BeforeInteractHandEvent ev)
    {
        if (ev.Handled || state.ActiveWristTool is not {} tool || _hands.GetActiveHand(user) is not { HeldEntity: null } ||
            Transform(tool).ParentUid != user || !_interaction.InRangeAndAccessible(user, ev.Target)) return;
        if (HasComp<WelderComponent>(tool))
            ev.Handled = _interaction.InteractUsing(user, tool, ev.Target, Transform(ev.Target).Coordinates);
        else if (_slots.GetItemOrNull(tool, "gun_magazine") is {} reservoir)
            ev.Handled = RefuelWristFlamer(ev.Target, reservoir);
    }

    private bool ServiceWrist(EntityUid plate, InteractUsingEvent ev)
    {
        foreach (var uid in Installed(plate))
        {
            var mod = Comp<PowerArmorModComponent>(uid);
            if (mod.ToolEntity is not {} tool) continue;
            if (mod.Kind == ArmorModKind.Shotgun && _tags.HasTag(ev.Used, "N14ShellShotgun12"))
                ev.Handled = _interaction.InteractUsing(ev.User, ev.Used, tool, ev.ClickLocation);
            else if (mod.Kind == ArmorModKind.Flamer && _slots.GetItemOrNull(tool, "gun_magazine") is {} reservoir)
                ev.Handled = RefuelWristFlamer(ev.Used, reservoir);
            if (ev.Handled) return true;
        }
        return false;
    }

    private bool RefuelWristFlamer(EntityUid source, EntityUid reservoir)
    {
        if (source == reservoir || !_solutions.TryGetDrainableSolution(source, out var from, out var supply) ||
            !_solutions.TryGetSolution(reservoir, "fuel", out var to, out var tank)) return false;
        var available = supply.GetTotalPrototypeQuantity("WeldingFuel");
        if (available <= 0 || tank.AvailableVolume <= 0) return false;
        _solutions.TryAddReagent(to.Value, "WeldingFuel", available, out var accepted);
        _solutions.RemoveReagent(from.Value, "WeldingFuel", accepted);
        return accepted > 0;
    }

    public void ExaminePlate(Entity<PowerArmorPlateComponent> plate, ref ExaminedEvent ev)
    {
        foreach (var uid in Installed(plate)) ev.PushText(Loc.GetString("pa-mod-fitted", ("mod", uid)));
        DescribeBuild(plate, ev);
    }

    public void ExamineFrame(Entity<ModularPowerArmorComponent> frame, ref ExaminedEvent ev)
    {
        var mods = Fitted(frame).ToArray();
        ev.PushText(Loc.GetString("pa-mod-capacity", ("count", mods.Length), ("max", frame.Comp.ModCapacity)));
        foreach (var uid in mods) ev.PushText(Loc.GetString("pa-mod-fitted", ("mod", uid)));
        Scan(frame, ev);
    }

    private void Scan(EntityUid frame, ExaminedEvent ev)
    {
        if (!TryComp<PowerArmorModWearerComponent>(ev.Examiner, out var wearer) || !wearer.MechanicVisor) return;
        foreach (var slot in PowerArmorPlates.Slots)
        {
            var name = Loc.GetString("pa-slot-" + slot.ToString().ToLowerInvariant());
            if (_slots.GetItemOrNull(frame, PowerArmorPlates.Container(slot)) is not {} plate)
            { ev.PushText(Loc.GetString("pa-mod-scan-missing", ("part", name))); continue; }
            var max = Comp<PowerArmorPlateComponent>(plate).HitPoints;
            var hp = Math.Max(0, max - (int)Math.Ceiling(Comp<DamageableComponent>(plate).TotalDamage.Float()));
            ev.PushText(Loc.GetString("pa-mod-scan-health", ("part", name), ("hp", hp), ("max", max)));
        }
        var charge = _power.GetSuitPowerSystems(frame) is {} rig &&
            TryComp<BatteryComponent>(_slots.GetItemOrNull(rig, rig.Comp.SlotId), out var cell) && cell.MaxCharge > 0
            ? (int)Math.Round(100 * cell.CurrentCharge / cell.MaxCharge) : 0;
        ev.PushText(Loc.GetString("pa-mod-scan-core", ("charge", charge)));
    }
}
