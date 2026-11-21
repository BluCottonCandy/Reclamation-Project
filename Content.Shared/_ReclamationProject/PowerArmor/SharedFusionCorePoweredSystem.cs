using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Shared._ReclamationProject.PowerArmor;

public abstract class SharedFusionCorePoweredSystem : EntitySystem
{
    [Dependency] protected SharedActionsSystem Actions = default!;
    [Dependency] protected MovementSpeedModifierSystem Movement = default!;
    [Dependency] private SharedSpecialSystem _special = default!;
    [Dependency] protected InventorySystem Inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FusionCorePoweredComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshSpeed);
        SubscribeLocalEvent<ModularPowerArmorComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnFrameSpeed);
        SubscribeLocalEvent<PowerArmorProficiencyComponent, ComponentStartup>(OnTrainingChanged);
        SubscribeLocalEvent<PowerArmorProficiencyComponent, ComponentShutdown>(OnTrainingChanged);
        SubscribeLocalEvent<FusionCorePoweredComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<FusionCorePoweredComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnRefreshSpeed(Entity<FusionCorePoweredComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        var wearer = Transform(ent).ParentUid;
        if (!Inventory.TryGetSlotEntity(wearer, "back", out var back) || back != ent.Owner)
            return;
        if (Inventory.TryGetSlotEntity(wearer, "outerClothing", out var shell) && HasComp<ModularPowerArmorComponent>(shell))
            return; // The worn frame accounts for this pack.

        var strength = GetStrength(wearer);
        var speed = ent.Comp.Powered ? PoweredMultiplier(ent.Comp, strength)
            : strength >= ent.Comp.StrongMinimumStrength ? ent.Comp.StrongUnpoweredSpeedMultiplier : ent.Comp.UnpoweredSpeedMultiplier;
        args.Args.ModifySpeed(speed);
    }

    private void OnFrameSpeed(Entity<ModularPowerArmorComponent> frame, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        var wearer = Transform(frame).ParentUid;
        if (!Inventory.TryGetSlotEntity(wearer, "outerClothing", out var shell) || shell != frame.Owner)
            return;

        var power = GetDeployedRig(wearer)?.Comp ?? GetSuitPowerSystems(frame)?.Comp;
        if (power is not { Powered: true })
        {
            // Other effects can slow the wearer further, but cannot power the frame.
            args.Args.BaseSpeedMultiplier *= FusionCorePoweredComponent.UnpoweredFrameSpeed;
            args.Args.CapSpeed(FusionCorePoweredComponent.UnpoweredFrameSpeed);
            return;
        }

        if (!TryComp<PowerArmorProficiencyComponent>(wearer, out var training) || training.LifeStage > ComponentLifeStage.Running)
            args.Args.BaseSpeedMultiplier *= PoweredMultiplier(power, GetStrength(wearer)) * FusionCorePoweredComponent.StandardShellSpeed;
        if (frame.Comp.Overdrive)
            args.Args.ModifySpeed(1.1f);
    }

    private int GetStrength(EntityUid wearer) =>
        _special.UsesSpecialStats(wearer) ? _special.GetEffective(wearer, SpecialStat.Strength) : 0;

    private static float PoweredMultiplier(FusionCorePoweredComponent power, int strength)
    {
        if (strength >= power.MightyMinimumStrength)
            return power.MightyPoweredSpeedMultiplier;
        return strength >= power.StrongMinimumStrength ? power.StrongPoweredSpeedMultiplier : power.PoweredSpeedMultiplier;
    }

    private void OnTrainingChanged<T>(Entity<PowerArmorProficiencyComponent> wearer, ref T ev) where T : EntityEventArgs
    {
        if (!TerminatingOrDeleted(wearer) && HasComp<MovementSpeedModifierComponent>(wearer))
            Movement.RefreshMovementSpeedModifiers(wearer);
    }

    protected Entity<FusionCorePoweredComponent>? GetDeployedRig(EntityUid wearer) =>
        Inventory.TryGetSlotEntity(wearer, "back", out var back) && TryComp<FusionCorePoweredComponent>(back, out var power)
            ? (back.Value, power) : null;

    public Entity<FusionCorePoweredComponent>? GetSuitPowerSystems(EntityUid suit)
    {
        if (TryComp<ToggleableClothingComponent>(suit, out var kit))
            foreach (var child in kit.ClothingUids.Keys)
                if (TryComp<FusionCorePoweredComponent>(child, out var power))
                    return (child, power);
        return null;
    }

    protected EntityUid? GetWearer(EntityUid rig)
    {
        var parent = Transform(rig).ParentUid;
        if (HasComp<InventoryComponent>(parent))
            return Inventory.TryGetSlotEntity(parent, "back", out var back) && back == rig ? parent : null;
        if (!HasComp<ModularPowerArmorComponent>(parent)) return null;
        var wearer = Transform(parent).ParentUid;
        return HasComp<InventoryComponent>(wearer) && Inventory.TryGetSlotEntity(wearer, "outerClothing", out var suit)
            && suit == parent && GetDeployedRig(wearer) == null ? wearer : null;
    }

    private void OnGetActions(Entity<FusionCorePoweredComponent> ent, ref GetItemActionsEvent args)
    {
        if (args.SlotFlags != SlotFlags.BACK)
            return;

        args.AddAction(ref ent.Comp.PowerActionEntity, ent.Comp.PowerAction);
        Actions.SetToggled(ent.Comp.PowerActionEntity, ent.Comp.SwitchedOn);
    }

    private void OnState(Entity<FusionCorePoweredComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshWearer(ent);
    }

    protected void RefreshWearer(EntityUid piece)
    {
        if (GetWearer(piece) is {} wearer)
            Movement.RefreshMovementSpeedModifiers(wearer);
    }
}
