using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
// #Reclamation Project Add - Server half of crank-charged guns (Minutemen laser musket).
// Cell charge lives in the server-only BatteryComponent, so paying for a crank happens here.
using System.Diagnostics.CodeAnalysis;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._ReclamationProject.Weapons.Ranged;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._ReclamationProject.Weapons.Ranged;

public sealed partial class CrankChargeSystem : SharedCrankChargeSystem
{
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrankChargeComponent, AmmoShotEvent>(OnProjectilesFired);
    }

    private void OnProjectilesFired(EntityUid uid, CrankChargeComponent comp, AmmoShotEvent args)
    {
        if (comp.ProjectilePrototype == null || args.FiredProjectiles.Count == 0 ||
            GetBonusDamage(comp) is not { } bonus)
            return;

        // The configured bonus is for the whole blast, not once per pellet.
        var perPellet = bonus / args.FiredProjectiles.Count;
        foreach (var entity in args.FiredProjectiles)
        {
            if (!TryComp<ProjectileComponent>(entity, out var projectile))
                continue;

            projectile.Damage = new DamageSpecifier(projectile.Damage) + perPellet;
            Dirty(entity, projectile);
        }
    }

    protected override bool CanAffordCrank(Entity<CrankChargeComponent> ent, EntityUid user)
    {
        if (TryGetCrankCost(ent, out _, out _, out _))
            return true;

        Popup.PopupEntity(Loc.GetString("crank-charge-low"), ent, user);
        return false;
    }

    protected override bool TryPayCrankCost(Entity<CrankChargeComponent> ent, EntityUid user)
    {
        if (!TryGetCrankCost(ent, out var cell, out var battery, out var cost))
        {
            Popup.PopupEntity(Loc.GetString("crank-charge-low"), ent, user);
            return false;
        }

        return _battery.TryUseCharge(cell, cost, battery);
    }

    /// <summary>
    /// A crank costs CrankCostFraction of the cell's fire cost, and the cell must still hold
    /// one full shot afterwards, so a cranked shot can always fire.
    /// </summary>
    private bool TryGetCrankCost(Entity<CrankChargeComponent> ent,
        out EntityUid cell,
        [NotNullWhen(true)] out BatteryComponent? battery,
        out float cost)
    {
        cell = default;
        battery = null;
        cost = 0f;

        if (_itemSlots.GetItemOrNull(ent, MagazineSlot) is not { } item ||
            !TryComp(item, out battery) ||
            !TryComp<HitscanBatteryAmmoProviderComponent>(item, out var provider))
        {
            return false;
        }

        cell = item;
        cost = provider.FireCost * ent.Comp.CrankCostFraction;
        return battery.CurrentCharge - cost >= provider.FireCost;
    }
}
