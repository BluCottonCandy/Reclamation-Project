using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Actions;
using Content.Shared.Clothing;
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
        Inventory.TryGetSlotEntity(wearer, "outerClothing", out var shell);
        TryComp<ClothingSpeedModifierComponent>(ent, out var rigBoost);
        ApplySpeed(wearer, shell, ent.Comp, args.Args, rigBoost);
    }

    private void OnFrameSpeed(Entity<ModularPowerArmorComponent> frame, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        var wearer = Transform(frame).ParentUid;
        if (HasDeployedRig(wearer) || GetSuitPowerSystems(frame) is not {} power)
            return;
        ApplySpeed(wearer, frame.Owner, power.Comp, args.Args, null);
    }

    private void ApplySpeed(EntityUid wearer, EntityUid? shell, FusionCorePoweredComponent power,
        RefreshMovementSpeedModifiersEvent args, ClothingSpeedModifierComponent? rigBoost)
    {
        var strength = _special.UsesSpecialStats(wearer) ? _special.GetEffective(wearer, SpecialStat.Strength) : 0;
        var walk = power.Powered
            ? strength >= power.MightyMinimumStrength ? power.MightyPoweredSpeedMultiplier
                : strength >= power.StrongMinimumStrength ? power.StrongPoweredSpeedMultiplier : power.PoweredSpeedMultiplier
            : strength >= power.StrongMinimumStrength ? power.StrongUnpoweredSpeedMultiplier : power.UnpoweredSpeedMultiplier;
        var sprint = walk;
        if (power.Powered && HasComp<ModularPowerArmorComponent>(shell) &&
            TryComp<PowerArmorProficiencyComponent>(wearer, out var training) && training.LifeStage <= ComponentLifeStage.Running &&
            TryComp<ClothingSpeedModifierComponent>(shell, out var shellSpeed))
        {
            // Cancel only the armor's own penalty. Injuries and other equipment still affect the wearer.
            walk = shellSpeed.WalkModifier > 0 ? 1f / shellSpeed.WalkModifier : 1f;
            sprint = shellSpeed.SprintModifier > 0 ? 1f / shellSpeed.SprintModifier : 1f;
        }
        if (rigBoost is { WalkModifier: > 0 }) walk /= rigBoost.WalkModifier;
        if (rigBoost is { SprintModifier: > 0 }) sprint /= rigBoost.SprintModifier;
        var overdrive = power.Powered && TryComp<ModularPowerArmorComponent>(shell, out var frame) && frame.Overdrive ? 1.1f : 1f;
        args.ModifySpeed(walk * overdrive, sprint * overdrive);
    }

    private void OnTrainingChanged<T>(Entity<PowerArmorProficiencyComponent> wearer, ref T ev) where T : EntityEventArgs
    {
        if (!TerminatingOrDeleted(wearer) && HasComp<MovementSpeedModifierComponent>(wearer))
            Movement.RefreshMovementSpeedModifiers(wearer);
    }

    private bool HasDeployedRig(EntityUid wearer) =>
        Inventory.TryGetSlotEntity(wearer, "back", out var back) && HasComp<FusionCorePoweredComponent>(back);

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
            && suit == parent && !HasDeployedRig(wearer) ? wearer : null;
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
