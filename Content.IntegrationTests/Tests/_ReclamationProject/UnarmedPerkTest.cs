using Content.Server.Traits;
using Content.Shared.Traits;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class UnarmedPerkTest
{
    [TestCase("MobHuman", "N14IronFist", "Blunt", 18, -6)]
    [TestCase("N14MobGhoul", "N14RadClaws", "Radiation", 18, -6)]
    [TestCase("N14MobGhoulGlowing", "N14RadClaws", "Radiation", 18, -6)]
    [TestCase("MobHuman", "N14PillowKnuckles", "Blunt", 0, 1)]
    [TestCase("N14MobGhoulGlowing", "N14PillowKnuckles", "Blunt", 0, 1)]
    public async Task PerksReplaceInnateDamageWithoutChangingHeldWeapons(string mob, string traitId,
        string damageType, int damage, int points)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity(mob, map.GridCoords);
            var heldWeapon = entities.SpawnEntity("Crowbar", map.GridCoords);
            var weapon = entities.GetComponent<MeleeWeaponComponent>(heldWeapon);
            var originalWeaponDamage = weapon.Damage.GetTotal();
            var originalUnarmed = entities.GetComponent<MeleeWeaponComponent>(user);
            var originalWideSwing = originalUnarmed.CanWideSwing;
            var originalMaxTargets = originalUnarmed.MaxTargets;
            var originalAngle = originalUnarmed.Angle;
            var perk = prototypes.Index<TraitPrototype>(traitId);
            entities.System<TraitSystem>().AddTrait(user, perk);
            var unarmed = entities.GetComponent<MeleeWeaponComponent>(user);
            Assert.That(perk.Points, Is.EqualTo(points));
            Assert.That((float) unarmed.Damage.GetTotal(), Is.EqualTo(damage));
            Assert.That((float) unarmed.Damage.DamageDict[damageType], Is.EqualTo(damage));
            Assert.That(weapon.Damage.GetTotal(), Is.EqualTo(originalWeaponDamage));
            Assert.That(unarmed.CanWideSwing, Is.EqualTo(originalWideSwing));
            Assert.That(unarmed.MaxTargets, Is.EqualTo(originalMaxTargets));
            Assert.That(unarmed.Angle, Is.EqualTo(originalAngle));
            entities.DeleteEntity(user);
            entities.DeleteEntity(heldWeapon);
        });
        await pair.CleanReturnAsync();
    }
}

