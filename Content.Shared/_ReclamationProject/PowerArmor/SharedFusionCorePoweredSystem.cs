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
        // Replace the power systems' own speed boost with a Strength-based speed:
        // powered 70/75/80%, unpowered a crawl (4%, or 5% at Strength 8+).
        var wearer = Transform(ent).ParentUid;
        var strength = _special.UsesSpecialStats(wearer) ? _special.GetEffective(wearer, SpecialStat.Strength) : 0;
        float walk;
        if (ent.Comp.Powered)
        {
            walk = strength >= ent.Comp.MightyMinimumStrength ? ent.Comp.MightyPoweredSpeedMultiplier
                : strength >= ent.Comp.StrongMinimumStrength ? ent.Comp.StrongPoweredSpeedMultiplier
                : ent.Comp.PoweredSpeedMultiplier;
        }
        else
        {
            walk = strength >= ent.Comp.StrongMinimumStrength ? ent.Comp.StrongUnpoweredSpeedMultiplier
                : ent.Comp.UnpoweredSpeedMultiplier;
        }

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
