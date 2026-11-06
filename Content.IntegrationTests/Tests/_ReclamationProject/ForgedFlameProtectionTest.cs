using System;
using Content.Shared.Armor;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.IoC;
namespace Content.IntegrationTests.Tests._ReclamationProject;
[TestFixture]
public sealed class ForgedFlameProtectionTest
{
    [Test]
    public async System.Threading.Tasks.Task FlamesAreResistedAndLasersUseModestHeatArmor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() => Verify(entities, map.GridCoords, (ok, message) => Assert.That(ok, Is.True, message)));
        await pair.CleanReturnAsync();
    }
    private static void Verify(IEntityManager entities, EntityCoordinates coordinates, Action<bool, string> check)
    {
        var inventory = entities.System<InventorySystem>();
        var movement = entities.System<MovementSpeedModifierSystem>();
        var damage = entities.System<DamageableSystem>();
        foreach (var entry in new[]
        {
            (Suit: "ForgedFurnaceArmor", Head: "ForgedFurnaceHelmet", Flame: .05f, Walk: .85f, Sprint: .85f, Pierce: .65f, Blast: .05f, Heavy: true),
            (Suit: "ForgedKeeperFurnaceArmor", Head: "ForgedKeeperFurnaceHelmet", Flame: .05f, Walk: .80f, Sprint: .80f, Pierce: .55f, Blast: .05f, Heavy: true),
            (Suit: "ForgedTemperedFirefighterArmor", Head: "ForgedTemperedFirefighterHood", Flame: .10f, Walk: 1f, Sprint: .90f, Pierce: .55f, Blast: .10f, Heavy: false)
        })
        {
            var user = entities.SpawnEntity("MobHuman", coordinates);
            var speed = entities.GetComponent<MovementSpeedModifierComponent>(user);
            movement.RefreshMovementSpeedModifiers(user);
            var walk = speed.WalkSpeedModifier;
            var sprint = speed.SprintSpeedModifier;
            EntityUid Equip(string id, string slot)
            {
                inventory.TryUnequip(user, slot, silent: true, force: true);
                var item = entities.SpawnEntity(id, coordinates);
                check(inventory.TryEquip(user, item, slot, silent: true, force: true), id + " equip " + slot);
                return item;
            }
            var suit = Equip(entry.Suit, "outerClothing");
            var armor = entities.GetComponent<ArmorComponent>(suit);
            check(Math.Abs(armor.Modifiers.Coefficients["Piercing"] - entry.Pierce) < .001f, entry.Suit + " piercing rating");
            check(Math.Abs(armor.Modifiers.Coefficients["Heat"] - .85f) < .001f, entry.Suit + " supplies 15% ordinary heat armor");
            var message = new Robust.Shared.Utility.FormattedMessage();
            var examine = new ArmorExamineEvent(message);
            entities.EventBus.RaiseLocalEvent(suit, ref examine);
            check(message.ToString().Contains("Fire") && message.ToString().Contains(entry.Heavy ? "95" : "90"), "Fire protection missing from armor details");
            if (entry.Heavy)
            {
                check(Math.Abs(armor.Modifiers.Coefficients["Blunt"] - entry.Pierce) < .001f && Math.Abs(armor.Modifiers.Coefficients["Slash"] - entry.Pierce) < .001f, "Conventional suit protection cut");
                check(Math.Abs(armor.Modifiers.Coefficients["Radiation"] - .70f) < .001f && Math.Abs(armor.Modifiers.Coefficients["Caustic"] - .80f) < .001f, "Radiation and caustic protection cut");
            }
            Equip(entry.Head, "head");
            Equip("ForgedGasMask", "mask");
            if (entry.Heavy)
            {
                Equip("N14ClothingPABootsDark", "shoes");
                Equip("N14ClothingPAGauntletsDark", "gloves");
            }
            else
            {
                Equip("ForgedCombatBoots", "shoes");
                Equip("ForgedLeatherGloves", "gloves");
            }
            movement.RefreshMovementSpeedModifiers(user);
            check(Math.Abs(speed.WalkSpeedModifier / walk - entry.Walk) < .001f, entry.Suit + " walking speed");
            check(Math.Abs(speed.SprintSpeedModifier / sprint - entry.Sprint) < .001f, entry.Suit + " running speed");

            var protection = new GetFireProtectionEvent();
            entities.EventBus.RaiseLocalEvent(user, ref protection);
            inventory.RelayEvent((user, entities.GetComponent<InventoryComponent>(user)), ref protection);
            check(Math.Abs(protection.Multiplier - entry.Flame) < .001f, entry.Suit + " full outfit flame cap");

            foreach (var projectileId in new[] { "BulletFlame", "BulletIncinerator" })
            {
                var projectile = entities.SpawnEntity(projectileId, coordinates);
                var spec = entities.GetComponent<ProjectileComponent>(projectile).Damage;
                var originalHeat = spec.DamageDict["Heat"];
                var hit = new ProjectileHitEvent(spec, user);
                entities.EventBus.RaiseLocalEvent(projectile, ref hit);
                check(Math.Abs((hit.Damage.DamageDict["Heat"] / originalHeat).Float() - entry.Flame) < .002f, entry.Suit + " flame projectile reduction");
                check(spec.DamageDict["Heat"] == originalHeat, "Original projectile damage was mutated");
                var applied = damage.TryChangeDamage(user, hit.Damage, doPartDamage: false);
                check(applied != null && applied.DamageDict["Heat"].Float() > 0f && applied.DamageDict["Heat"].Float() <= originalHeat.Float() * .11f, "Flame impact remains damaging but strongly resisted");
                var mixed = new DamageSpecifier { DamageDict = new() { ["Heat"] = 10, ["Blunt"] = 5 } };
                var mixedHit = new ProjectileHitEvent(mixed, user);
                entities.EventBus.RaiseLocalEvent(projectile, ref mixedHit);
                check(mixedHit.Damage.DamageDict["Blunt"] == 5 && mixed.DamageDict["Heat"] == 10, "Non-heat damage and source spec remain unchanged");
                entities.DeleteEntity(projectile);
            }
            var laser = entities.SpawnEntity("BulletLaser", coordinates);
            check(!entities.HasComponent<FlameProjectileComponent>(laser), "Laser must not be marked as flame");
            var laserSpec = entities.GetComponent<ProjectileComponent>(laser).Damage;
            var laserHit = new ProjectileHitEvent(laserSpec, user);
            entities.EventBus.RaiseLocalEvent(laser, ref laserHit);
            check(laserHit.Damage.DamageDict["Heat"] == 20, "Fire protection altered laser impact");
            var laserApplied = damage.TryChangeDamage(user, laserHit.Damage, doPartDamage: false);
            check(laserApplied != null && Math.Abs(laserApplied.DamageDict["Heat"].Float() - (entry.Heavy ? 15.34f : 17f)) < .02f, entry.Suit + " laser damage uses modest heat armor and existing accessories");
            entities.DeleteEntity(laser);

            var ordinary = new GetExplosionResistanceEvent("Default");
            var incinerator = new GetExplosionResistanceEvent("IncineratorExplosion");
            entities.EventBus.RaiseLocalEvent(user, ref ordinary);
            entities.EventBus.RaiseLocalEvent(user, ref incinerator);
            check(Math.Abs(incinerator.DamageCoefficient / ordinary.DamageCoefficient - entry.Blast) < .001f, "Dedicated blast protection must not extend to every explosion");
            var blast = new DamageSpecifier { DamageDict = new() { ["Heat"] = Content.Shared.FixedPoint.FixedPoint2.New(8 * incinerator.DamageCoefficient) } };
            var blastApplied = damage.TryChangeDamage(user, blast, doPartDamage: false);
            check(blastApplied != null && blastApplied.DamageDict["Heat"].Float() > 0 && blastApplied.DamageDict["Heat"].Float() < 1, "Incinerator blast resisted without immunity");
            entities.QueueDeleteEntity(user);
        }
        var aerProfile = IoCManager.Resolve<Robust.Shared.Prototypes.IPrototypeManager>().Index<Content.Shared.Weapons.Ranged.HitscanPrototype>("N14AER9AutoLaser");
        foreach (var comparison in new[] { (Suit: "ForgedFurnaceArmor", Expected: 17f), (Suit: "ForgedKeeperFurnaceArmor", Expected: 17f), (Suit: "ForgedTemperedFirefighterArmor", Expected: 17f), (Suit: "ForgedUnbrandedArmor", Expected: 16f), (Suit: "ForgedBrandedCombatArmor", Expected: 15f) })
        {
            var target = entities.SpawnEntity("MobHuman", coordinates);
            var suit = entities.SpawnEntity(comparison.Suit, coordinates);
            check(inventory.TryEquip(target, suit, "outerClothing", silent: true, force: true), "AER comparison equip");
            var applied = damage.TryChangeDamage(target, new DamageSpecifier(aerProfile.Damage ?? throw new Exception("AER profile has no damage")));
            check(applied != null && Math.Abs(applied.GetTotal().Float() - comparison.Expected) < .01f, comparison.Suit + " AER hitscan heat resistance");
            entities.QueueDeleteEntity(target);
        }
        var smithy = entities.SpawnEntity("ForgedSmithyPowerArmor", coordinates);
        var adminArmor = entities.GetComponent<ArmorComponent>(smithy);
        check(adminArmor.Modifiers.Coefficients["Piercing"] == .20f && adminArmor.Modifiers.Coefficients["Heat"] == .20f, "Admin Smithy suit ratings changed");
        entities.QueueDeleteEntity(smithy);
        var smithyHead = entities.SpawnEntity("ForgedSmithyFurnaceHelmet", coordinates);
        var adminHelmet = entities.GetComponent<ArmorComponent>(smithyHead);
        check(adminHelmet.Modifiers.Coefficients["Piercing"] == .45f && adminHelmet.Modifiers.Coefficients["Heat"] == .20f, "Admin Smithy helmet ratings changed");
        entities.QueueDeleteEntity(smithyHead);

        var cap = new GetFireProtectionEvent();
        cap.Reduce(.95f, .95f); cap.Reduce(.5f);
        check(Math.Abs(cap.Multiplier - .05f) < .001f, "Additional clothing bypassed the cap");
        var reversed = new GetFireProtectionEvent();
        reversed.Reduce(.5f); reversed.Reduce(.95f, .95f);
        check(Math.Abs(reversed.Multiplier - cap.Multiplier) < .001f, "Cap depends on equipment order");
        var fireproof = new GetFireProtectionEvent();
        fireproof.Reduce(1f); fireproof.Reduce(.5f);
        check(fireproof.Multiplier == 0f, "Stacked fireproof clothing must never heal");
    }
}
