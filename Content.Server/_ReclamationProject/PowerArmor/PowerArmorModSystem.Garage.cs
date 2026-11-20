using System.Linq;
using Content.Server._ReclamationProject.Supplies;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stacks;
using Content.Shared.Standing;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.PowerArmor;

public sealed partial class PowerArmorModSystem
{
    [Dependency] private readonly BatterySystem _batteries = default!;
    [Dependency] private readonly PullingSystem _pull = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _clock = default!;

    private void InitializeGarageMods()
    {
        SubscribeLocalEvent<PowerArmorModComponent, TargetGarageModEvent>(OnTargetGarageMod);
        SubscribeLocalEvent<PowerArmorModComponent, JumpStartDoAfterEvent>(OnJumpStartComplete);
        SubscribeLocalEvent<ArmorTowComponent, PullStoppedMessage>((uid, tow, ev) =>
        {
            if (ev.PullerUid == uid && ev.PulledUid == tow.Target)
                ClearTow(uid);
        });
        SubscribeLocalEvent<PullableComponent, StoodEvent>((uid, pullable, _) =>
        {
            if (TryComp<ArmorTowComponent>(pullable.Puller, out var tow) && tow.Target == uid)
                _pull.TryStopPull(uid, pullable);
        });
    }

    private bool WornModule(Entity<PowerArmorModComponent> module, EntityUid user, out EntityUid frame)
    {
        frame = default;
        if (module.Comp.Wearer != user || !_inventory.TryGetSlotEntity(user, "outerClothing", out var outer) ||
            !HasComp<ModularPowerArmorComponent>(outer) || !Fitted(outer.Value).Contains(module.Owner) ||
            !InUse(outer.Value, user, module.Comp))
            return false;
        frame = outer.Value;
        return true;
    }

    private void ReleaseGarageMod(PowerArmorModComponent mod)
    {
        if (mod.Wearer is not {} user) return;
        switch (mod.Kind)
        {
            case ArmorModKind.MagLock:
                RemComp<ArmorMagLockComponent>(user);
                UpdateArmorGait(user);
                _actions.SetToggled(mod.ActionEntity, false);
                break;
            case ArmorModKind.TowRig:
                if (TryComp<ArmorTowComponent>(user, out var tow))
                {
                    _pull.TryStopPull(tow.Target, user: user);
                    ClearTow(user);
                }
                break;
            case ArmorModKind.Junkhound:
                RemComp<JunkhoundPulseComponent>(user);
                break;
        }
    }

    private void ToggleMagLock(EntityUid user, PowerArmorModComponent mod)
    {
        var engage = !HasComp<ArmorMagLockComponent>(user);
        if (engage)
            EnsureComp<ArmorMagLockComponent>(user);
        else
            RemComp<ArmorMagLockComponent>(user);
        UpdateArmorGait(user);
        _actions.SetToggled(mod.ActionEntity, engage);
    }

    private void OnTargetGarageMod(Entity<PowerArmorModComponent> mod, ref TargetGarageModEvent ev)
    {
        if (ev.Handled || !WornModule(mod, ev.Performer, out var frame) ||
            !_interaction.InRangeAndAccessible(ev.Performer, ev.Target)) return;
        switch (mod.Comp.Kind)
        {
            case ArmorModKind.JumperCables:
                ev.Handled = StartJumpStart(mod, ev.Performer, frame, ev.Target);
                break;
            case ArmorModKind.TowRig:
                ev.Handled = Hitch(ev.Performer, ev.Target);
                break;
        }
    }

    private EntityUid? CoreInFrame(EntityUid frame) => _power.GetSuitPowerSystems(frame) is {} rig
        ? _slots.GetItemOrNull(rig, rig.Comp.SlotId) : null;

    private EntityUid? ReceivingCore(EntityUid target)
    {
        if (HasComp<ModularPowerArmorComponent>(target))
            return CoreInFrame(target);
        if (HasComp<Content.Shared.Inventory.InventoryComponent>(target) &&
            _inventory.TryGetSlotEntity(target, "outerClothing", out var suit) && HasComp<ModularPowerArmorComponent>(suit))
            return CoreInFrame(suit.Value);
        return null;
    }

    private bool StartJumpStart(Entity<PowerArmorModComponent> mod, EntityUid user, EntityUid frame, EntityUid target)
    {
        if (CoreInFrame(frame) is not {} source || ReceivingCore(target) is not {} receiver ||
            source == receiver || !_tags.HasTag(source, "N14FusionCore") || !_tags.HasTag(receiver, "N14FusionCore") ||
            !TryComp<BatteryComponent>(source, out var donor) || !TryComp<BatteryComponent>(receiver, out var recipient) ||
            donor.CurrentCharge <= 0 || recipient.CurrentCharge >= recipient.MaxCharge)
        {
            Notice(user, "pa-mod-jumper-unavailable");
            return false;
        }
        var finish = new JumpStartDoAfterEvent { SourceCore = GetNetEntity(source), ReceivingCore = GetNetEntity(receiver) };
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(3), finish, mod, target)
        {
            BreakOnMove = true, BreakOnDamage = true, MultiplyDelay = false, DistanceThreshold = 1.5f,
            CancelDuplicate = false, DuplicateCondition = DuplicateConditions.SameEvent
        });
    }

    private void OnJumpStartComplete(Entity<PowerArmorModComponent> mod, ref JumpStartDoAfterEvent ev)
    {
        if (ev.Handled || ev.Cancelled || ev.Target is not {} target || !WornModule(mod, ev.User, out var frame) ||
            !_interaction.InRangeAndAccessible(ev.User, target) ||
            CoreInFrame(frame) is not {} source || ReceivingCore(target) is not {} receiver ||
            GetNetEntity(source) != ev.SourceCore || GetNetEntity(receiver) != ev.ReceivingCore ||
            !TryComp<BatteryComponent>(source, out var donor) || !TryComp<BatteryComponent>(receiver, out var recipient))
            return;
        var amount = MathF.Min(donor.CurrentCharge, MathF.Min(recipient.MaxCharge * 0.25f, recipient.MaxCharge - recipient.CurrentCharge));
        if (amount <= 0) return;
        _batteries.SetCharge(source, donor.CurrentCharge - amount, donor);
        _batteries.SetCharge(receiver, recipient.CurrentCharge + amount, recipient);
        ev.Handled = true;
        _popup.PopupEntity(Loc.GetString("pa-mod-jumper-done", ("percent", MathF.Round(100f * amount / recipient.MaxCharge))), ev.User, ev.User);
    }

    private bool Hitch(EntityUid user, EntityUid target)
    {
        if (TryComp<ArmorTowComponent>(user, out var current))
        {
            if (current.Target == target || target == user)
                return _pull.TryStopPull(current.Target, user: user);
            Notice(user, "pa-mod-tow-release");
            return false;
        }
        if (target == user || !IsTowable(target) || !_hands.TryGetEmptyHand(user, out _) ||
            !TryComp<PullerComponent>(user, out var puller) || puller.Pulling != null)
        {
            Notice(user, "pa-mod-tow-unavailable");
            return false;
        }
        var tow = EnsureComp<ArmorTowComponent>(user);
        tow.Target = target;
        Dirty(user, tow);
        if (!_pull.TryStartPull(user, target, puller))
        {
            ClearTow(user);
            return false;
        }
        UpdateArmorGait(user);
        _movement.RefreshMovementSpeedModifiers(user);
        Notice(user, "pa-mod-tow-attached");
        return true;
    }

    private bool IsTowable(EntityUid target)
    {
        if (HasComp<MobStateComponent>(target)) return _standing.IsDown(target);
        return HasComp<FactionSupplyCrateComponent>(target) || MetaData(target).EntityPrototype is {} proto &&
            _prototypes.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true).Any(p => p.id == "CrateGeneric");
    }

    private void ClearTow(EntityUid user)
    {
        if (!RemComp<ArmorTowComponent>(user) || TerminatingOrDeleted(user)) return;
        UpdateArmorGait(user);
        _movement.RefreshMovementSpeedModifiers(user);
    }

    private void UpdateArmorGait(EntityUid user)
    {
        if (TerminatingOrDeleted(user) || !TryComp<Content.Shared.Movement.Components.InputMoverComponent>(user, out var mover))
            return;
        var walk = HasComp<ArmorTowComponent>(user) || HasComp<ArmorMagLockComponent>(user);
        if (mover.ArmorForcesWalk == walk) return;
        mover.ArmorForcesWalk = walk;
        Dirty(user, mover);
    }

    private bool PulseJunkhound(EntityUid user)
    {
        var pulse = EnsureComp<JunkhoundPulseComponent>(user);
        if (pulse.Until > _clock.CurTime) return false;
        pulse.Until = _clock.CurTime + JunkhoundPulseComponent.Duration;
        var origin = Transform(user).Coordinates;
        pulse.Hits.Clear();
        foreach (var item in _lookup.GetEntitiesInRange(origin, JunkhoundPulseComponent.Range, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (_containers.IsEntityInContainer(item) || !IsSalvage(item) ||
                !origin.TryDistance(EntityManager, Transform(item).Coordinates, out var distance) || distance > JunkhoundPulseComponent.Range)
                continue;
            pulse.Hits.Add(item);
        }
        Dirty(user, pulse);
        return true;
    }

    private bool IsSalvage(EntityUid item)
    {
        if (HasComp<PowerArmorPlateComponent>(item)) return true;
        if (!TryComp<StackComponent>(item, out var stack)) return false;
        var id = stack.StackTypeId.Id;
        return id.StartsWith("N14Scrap", StringComparison.Ordinal) || id is
            "Steel" or "Plasteel" or "MetalRod" or "Aluminum" or "Lead" or "N14Iron" or "N14Copper" or
            "N14Brass" or "N14Gold" or "N14Silver" or "Gold" or "Silver" or "Uranium" or "N14LeadOre" or
            "N14CopperOre" or "N14IronOre" or "N14GoldOre" or "N14SilverOre" or "GoldOre" or "SteelOre" or "SilverOre" or "UraniumOre";
    }
}
