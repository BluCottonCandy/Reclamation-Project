using System;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
namespace Content.IntegrationTests.Tests._ReclamationProject;
[TestFixture]
public sealed class HeavyTrooperEquipmentTest
{
    [Test]
    public async System.Threading.Tasks.Task ChestKnifeAndArmor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() => Verify(entities, map.GridCoords, (ok, message) => Assert.That(ok, Is.True, message)));
        await pair.CleanReturnAsync();
    }
    private static void Verify(IEntityManager entities, EntityCoordinates coords, Action<bool, string> check)
    {
        var inventory = entities.System<InventorySystem>();
        var hands = entities.System<SharedHandsSystem>();
        var slots = entities.System<ItemSlotsSystem>();
        var movement = entities.System<MovementSpeedModifierSystem>();
        var damage = entities.System<DamageableSystem>();
        var user = entities.SpawnEntity("MobHuman", coords);
        var speed = entities.GetComponent<MovementSpeedModifierComponent>(user);
        movement.RefreshMovementSpeedModifiers(user);
        var walk = speed.WalkSpeedModifier;
        var sprint = speed.SprintSpeedModifier;
        var armor = entities.SpawnEntity("MinutemenHeavyTrooperArmor", coords);
        check(inventory.TryEquip(user, armor, "outerClothing", silent: true, force: true), "Equip armor");
        check(Math.Abs(speed.WalkSpeedModifier/walk - .85f) < .001f && Math.Abs(speed.SprintSpeedModifier/sprint - .85f) < .001f, "15% worn slowdown");
        var clothing = entities.GetComponent<ClothingComponent>(armor);
        check(slots.TryGetSlot(armor, "chest_knife", out var slot) && slot.Item != null, "Armor starts with a real knife");
        if (!slots.TryGetSlot(armor, "chest_knife", out slot) || slot.Item is not { } knife)
            throw new Exception("Missing initial knife");
        check(clothing.EquippedPrefix == "knife", "Initial worn knife visible");
        for (var i = 0; i < 3; i++)
        {
            check(slots.TryEjectToHands(armor, slot, user), "Draw knife through existing slot");
            check(clothing.EquippedPrefix == null, "Knife disappears while armor remains equipped");
            check(hands.IsHolding(user, knife, out _), "Same knife drawn into hand");
            check(slots.TryInsertFromHand(armor, slot, user), "Reinsert knife through existing slot");
            check(clothing.EquippedPrefix == "knife" && slot.Item == knife, "Same knife returns without duplication");
        }
        foreach (var type in new[] { "Heat", "Slash", "Piercing", "Blunt" })
        {
            var applied = damage.TryChangeDamage(user, new DamageSpecifier { DamageDict = new() { [type] = 10 } }, doPartDamage: false);
            check(applied != null && applied.GetTotal() == 5, "50% " + type + " protection");
        }
        var blast = new GetExplosionResistanceEvent("Default");
        entities.EventBus.RaiseLocalEvent(user, ref blast);
        check(Math.Abs(blast.DamageCoefficient - .4f) < .001f, "60% explosion protection");
        var blade = entities.SpawnEntity("MinutemenRotorBlade", coords);
        var reference = entities.SpawnEntity("N14SuperSledgeHammer", coords);
        var actual = entities.GetComponent<MeleeWeaponComponent>(blade).Damage;
        check(actual.DamageDict.Count == 1 && actual.DamageDict["Slash"] == 95, "Rotor blade deals exactly 95 slash");
        var knockback = entities.GetComponent<Content.Shared.Weapons.Melee.Components.MeleeThrowOnHitComponent>(blade);
        var sledge = entities.GetComponent<Content.Shared.Weapons.Melee.Components.MeleeThrowOnHitComponent>(reference);
        check(knockback.Speed == sledge.Speed && knockback.Lifetime == sledge.Lifetime, "Existing super-sledge knockback settings");
        var victim = entities.SpawnEntity("MobHuman", coords);
        var hit = new Content.Shared.Weapons.Melee.Events.MeleeHitEvent(new() { victim }, user, blade, actual, System.Numerics.Vector2.UnitX);
        entities.EventBus.RaiseLocalEvent(blade, hit);
        check(entities.TryGetComponent<Content.Shared.Weapons.Melee.Components.MeleeThrownComponent>(victim, out var thrown) && Math.Abs(thrown.Velocity.X - sledge.Speed) < .001f && thrown.Lifetime == sledge.Lifetime, "Melee hit actually applies super-sledge knockback");
        entities.QueueDeleteEntity(victim);
        check(!slots.TryInsert(armor, "chest_knife", blade, user), "Sheath rejects a non-knife weapon");
        check(slots.TryEjectToHands(armor, slot, user), "Draw knife for free-hand check");
        check(!hands.TryPickupAnyHand(user, blade), "Rotor blade requires two free hands");
        check(hands.TryDrop(user, knife), "Drop knife");
        check(hands.TryPickupAnyHand(user, blade), "Rotor blade can be picked up with two free hands");
        var shooter = entities.SpawnEntity("MobHuman", coords);
        var musket = entities.SpawnEntity("MinutemenWeaponHeavyLaserMusket", coords);
        check(entities.HasComponent<Content.Shared._Misfits.Scope.ScopeComponent>(musket), "Heavy musket reuses the existing scope");
        var gun = entities.GetComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(musket);
        var crank = entities.GetComponent<Content.Shared._ReclamationProject.Weapons.Ranged.CrankChargeComponent>(musket);
        check(crank.MaxCharges == 1 && crank.CrankDelay == TimeSpan.FromSeconds(3), "Heavy musket supports one three-second crank");
        check(crank.BonusByCharge.Count == 1 && crank.BonusByCharge[0] == 55, "Crank adds 55 to the 20-heat beam");
        check(hands.TryPickupAnyHand(shooter, musket), "Pick up heavy musket");
        var wieldable = entities.GetComponent<Content.Shared.Wieldable.Components.WieldableComponent>(musket);
        var attempt = new Content.Shared.Weapons.Ranged.Events.ShotAttemptedEvent { User = shooter, Used = (musket, gun) };
        entities.EventBus.RaiseLocalEvent(musket, ref attempt);
        check(attempt.Cancelled, "Unwielded, uncranked musket cannot fire");
        check(entities.System<Content.Shared.Wieldable.WieldableSystem>().TryWield(musket, wieldable, shooter), "Wield musket");
        attempt = new() { User = shooter, Used = (musket, gun) };
        entities.EventBus.RaiseLocalEvent(musket, ref attempt);
        check(!attempt.Cancelled, "Wielded uncranked musket may fire at base damage");
        crank.Charges = 1;
        attempt = new() { User = shooter, Used = (musket, gun) };
        entities.EventBus.RaiseLocalEvent(musket, ref attempt);
        check(!attempt.Cancelled, "Wielded charged musket may fire");
        var fired = new Content.Shared.Weapons.Ranged.Systems.GunShotEvent(shooter, new());
        entities.EventBus.RaiseLocalEvent(musket, ref fired);
        check(crank.Charges == 0, "Firing consumes crank charge");
        var gloves = entities.SpawnEntity("MinutemenHeavyTrooperGauntlets", coords);
        var claws = entities.GetComponent<MeleeWeaponComponent>(gloves);
        check(claws.Damage.DamageDict["Slash"] == 20 && claws.MustBeEquippedToUse, "Black gauntlets retain Priestess melee behavior");
        var mask = entities.SpawnEntity("ClothingMaskGasRanger", coords);
        check(entities.HasComponent<Content.Shared.Overlays.Switchable.NightVisionComponent>(mask), "Ranger mask retains night vision");
        var duster = entities.SpawnEntity("MinutemenClothingNeckDuster", coords);
        check(inventory.TryEquip(shooter, gloves, "gloves", silent: true, force: true), "Equip gauntlets");
        check(inventory.TryEquip(shooter, mask, "mask", silent: true, force: true), "Equip night vision mask");
        check(inventory.TryEquip(shooter, duster, "neck", silent: true, force: true), "Equip duster");
        entities.QueueDeleteEntity(shooter);
        entities.QueueDeleteEntity(user);
        entities.QueueDeleteEntity(reference);
        entities.QueueDeleteEntity(knife);
    }
}
