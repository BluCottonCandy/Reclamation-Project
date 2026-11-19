using Content.Shared.Armor;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Shared._ReclamationProject.PowerArmor;

/// <summary>Applies the old post-resistance absorption to the HP of a fitted plate.</summary>
public sealed class ModularArmorDamageSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ModularPowerArmorComponent, InventoryRelayedEvent<DamageModifyEvent>>(
            OnHit, after: new[] { typeof(SharedArmorSystem) });
    }

    private void OnHit(Entity<ModularPowerArmorComponent> suit, ref InventoryRelayedEvent<DamageModifyEvent> ev)
    {
        var hit = ev.Args;
        if (hit.ModularArmorApplied) return;
        hit.ModularArmorApplied = true;
        if (suit.Comp.Absorption <= 0 || !hit.Damage.AnyPositive()) return;

        // A single impact wears one installed piece. Excess damage on the breaking hit reaches the wearer.
        var plate = ImpactPlate(suit, hit.TargetPart);
        if (plate == null) return;
        var remaining = Comp<PowerArmorPlateComponent>(plate.Value).HitPoints - Comp<DamageableComponent>(plate.Value).TotalDamage;
        var absorbed = new DamageSpecifier();
        foreach (var (type, amount) in hit.Damage.DamageDict)
        {
            if (amount <= 0 || type is "Asphyxiation" or "Bloodloss" or "Cellular" or "Poison" or "Radiation")
                continue;
            var wear = FixedPoint2.Min(remaining, amount * suit.Comp.Absorption);
            if (wear <= 0) break;
            absorbed.DamageDict[type] = wear;
            remaining -= wear;
        }
        hit.Damage -= absorbed;
        if (_net.IsServer && absorbed.AnyPositive())
            _damage.TryChangeDamage(plate.Value, absorbed, ignoreResistances: true, doPartDamage: false);
    }

    public EntityUid? ImpactPlate(EntityUid suit, TargetBodyPart? target = null)
    {
        var region = target switch
        {
            TargetBodyPart.Head => ArmorPlateSlot.Helmet,
            TargetBodyPart.LeftArm or TargetBodyPart.RightArm => ArmorPlateSlot.Pauldrons,
            TargetBodyPart.LeftHand or TargetBodyPart.RightHand => ArmorPlateSlot.Gloves,
            TargetBodyPart.Torso or TargetBodyPart.Groin or TargetBodyPart.LeftLeg or TargetBodyPart.RightLeg
                or TargetBodyPart.LeftFoot or TargetBodyPart.RightFoot => ArmorPlateSlot.Chest,
            _ => (ArmorPlateSlot?) null
        };
        if (region is {} slot && _slots.GetItemOrNull(suit, PowerArmorPlates.Container(slot)) is {} targeted)
            return targeted;
        var available = new List<EntityUid>(4);
        foreach (var part in PowerArmorPlates.Slots)
            if (_slots.GetItemOrNull(suit, PowerArmorPlates.Container(part)) is {} item) available.Add(item);
        return available.Count > 0 ? _random.Pick(available) : null;
    }
}
