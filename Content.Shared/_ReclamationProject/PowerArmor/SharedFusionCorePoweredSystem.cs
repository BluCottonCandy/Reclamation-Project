using Content.Shared._Misfits.Special;
using Content.Shared.Actions;
using Content.Shared.Clothing;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;

namespace Content.Shared._ReclamationProject.PowerArmor;

public abstract class SharedFusionCorePoweredSystem : EntitySystem
{
    [Dependency] protected SharedActionsSystem Actions = default!;
    [Dependency] protected MovementSpeedModifierSystem Movement = default!;
    [Dependency] private SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FusionCorePoweredComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshSpeed);
        SubscribeLocalEvent<FusionCorePoweredComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<FusionCorePoweredComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnRefreshSpeed(Entity<FusionCorePoweredComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (ent.Comp.Powered)
            return;

        // Cancel the power systems' own speed boost, then slow further. Strong wearers haul it a bit better.
        var wearer = Transform(ent).ParentUid;
        var strong = _special.UsesSpecialStats(wearer)
            && _special.GetEffective(wearer, SpecialStat.Strength) >= ent.Comp.StrongMinimumStrength;
        var walk = strong ? ent.Comp.StrongUnpoweredSpeedMultiplier : ent.Comp.UnpoweredSpeedMultiplier;
        var sprint = walk;
        if (TryComp<ClothingSpeedModifierComponent>(ent, out var boost))
        {
            if (boost.WalkModifier > 0)
                walk /= boost.WalkModifier;
            if (boost.SprintModifier > 0)
                sprint /= boost.SprintModifier;
        }

        args.Args.ModifySpeed(walk, sprint);
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
        var parent = Transform(piece).ParentUid;
        if (parent.IsValid() && HasComp<InventoryComponent>(parent))
            Movement.RefreshMovementSpeedModifiers(parent);
    }
}
