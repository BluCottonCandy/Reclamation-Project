using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Events;
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class MirelurkQueenTest
{
    [Test]
    public async Task QueenHasInnateProtectionHealthDamageAndFasterMovement()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var normal = entities.SpawnEntity("N14MobMirelurk", map.GridCoords);
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var speed = entities.GetComponent<MovementSpeedModifierComponent>(queen);
            Assert.That(speed.BaseSprintSpeed, Is.GreaterThan(entities.GetComponent<MovementSpeedModifierComponent>(normal).BaseSprintSpeed));
            Assert.That(speed.BaseSprintSpeed, Is.GreaterThan(entities.GetComponent<MovementSpeedModifierComponent>(human).BaseSprintSpeed));
            Assert.That(entities.GetComponent<MobThresholdsComponent>(queen).Thresholds.Where(e => e.Value == MobState.Dead).Min(e => e.Key).Float(), Is.EqualTo(1000));
            var attackRate = entities.GetComponent<MeleeWeaponComponent>(queen).AttackRate;
            Assert.That(attackRate, Is.EqualTo(2 * entities.GetComponent<MeleeWeaponComponent>(normal).AttackRate));
            var reflection = entities.GetComponent<ReflectComponent>(queen);
            var normalReflection = entities.GetComponent<ReflectComponent>(normal);
            Assert.That(reflection.Innate, Is.True);
            Assert.That(reflection.Reflects.HasFlag(normalReflection.Reflects), Is.True);
            Assert.That(reflection.Reflects.HasFlag(ReflectType.Energy), Is.True);
            Assert.That(reflection.ReflectProb, Is.EqualTo(normalReflection.ReflectProb));
            Assert.That(reflection.ReflectProbByType[ReflectType.SmallCaliber], Is.EqualTo(normalReflection.ReflectProbByType[ReflectType.SmallCaliber]));
            Assert.That(reflection.ReflectProbByType[ReflectType.MediumCaliber], Is.EqualTo(normalReflection.ReflectProbByType[ReflectType.MediumCaliber]));
            Assert.That(reflection.ReflectProbByType[ReflectType.Energy], Is.EqualTo(0.5f));
            // Guarantee the probability roll in this fixture to exercise the actual AER-9 hitscan path.
            reflection.ReflectProbByType[ReflectType.Energy] = 1;
            var laser = prototypes.Index<HitscanPrototype>("N14AER9AutoLaser");
            var reflectionAttempt = new HitScanReflectAttemptEvent(human, normal, laser.Reflective, Vector2.UnitX, false);
            entities.EventBus.RaiseLocalEvent(queen, ref reflectionAttempt, true);
            Assert.That(reflectionAttempt.Reflected, Is.True);
            reflection.ReflectProbByType[ReflectType.Energy] = 0.5f;
            var initialWalkModifier = speed.WalkSpeedModifier;
            var initialSprintModifier = speed.SprintSpeedModifier;
            var attack = entities.GetComponent<MeleeWeaponComponent>(queen).Damage;
            Assert.That(attack.DamageDict["Blunt"].Float(), Is.EqualTo(20));
            Assert.That(attack.DamageDict["Slash"].Float(), Is.EqualTo(30));
            Assert.That(attack.GetTotal().Float(), Is.EqualTo(50));
            var damage = entities.System<DamageableSystem>();
            foreach (var type in new[] { "Blunt", "Slash", "Piercing", "Heat" })
            {
                var result = damage.TryChangeDamage(queen, new DamageSpecifier(prototypes.Index<DamageTypePrototype>(type), 100));
                Assert.That(result!.GetTotal().Float(), Is.EqualTo(50));
            }
            Assert.That(entities.GetComponent<MobStateComponent>(queen).CurrentState, Is.EqualTo(MobState.Alive));
            damage.TryChangeDamage(queen, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 1200));
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(initialWalkModifier));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(initialSprintModifier));
            damage.TryChangeDamage(queen, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 400));
            Assert.That(entities.GetComponent<DamageableComponent>(queen).TotalDamage.Float(), Is.EqualTo(1000));
            Assert.That(entities.GetComponent<MobStateComponent>(queen).CurrentState, Is.EqualTo(MobState.Dead));
            damage.TryChangeDamage(queen, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 10000));
            Assert.That(entities.EntityExists(queen), Is.True, "Overkill must leave the queen's corpse available for butchering.");
            Assert.That(entities.HasComponent<Content.Shared._Misfits.Traits.UngibbableComponent>(queen), Is.True);
            var drop = entities.GetComponent<ButcherableComponent>(queen).SpawnedEntities.Single(e => e.PrototypeId == "ReclamationMirelurkQueenCarapace");
            Assert.That(drop.Amount, Is.EqualTo(1));
            Assert.That(drop.SpawnProbability, Is.EqualTo(1));
            var hatchlingDrop = entities.GetComponent<ButcherableComponent>(queen).SpawnedEntities.Single(e => e.PrototypeId == "ReclamationMobBabyMirelurk");
            Assert.That(hatchlingDrop.Amount, Is.EqualTo(4));
            Assert.That(hatchlingDrop.SpawnProbability, Is.EqualTo(1));
            var baby = entities.SpawnEntity("ReclamationMobBabyMirelurk", map.GridCoords);
            var babyReflection = entities.GetComponent<ReflectComponent>(baby);
            Assert.That(babyReflection.Reflects, Is.EqualTo(ReflectType.None));
            Assert.That(babyReflection.ReflectProb, Is.Zero);
            Assert.That(babyReflection.ReflectProbByType.Values, Is.All.EqualTo(0f));
            var babyLaserAttempt = new HitScanReflectAttemptEvent(human, normal, laser.Reflective, Vector2.UnitX, false);
            entities.EventBus.RaiseLocalEvent(baby, ref babyLaserAttempt, true);
            Assert.That(babyLaserAttempt.Reflected, Is.False);
            var babySpeed = entities.GetComponent<MovementSpeedModifierComponent>(baby);
            var normalSpeed = entities.GetComponent<MovementSpeedModifierComponent>(normal);
            Assert.That(babySpeed.BaseWalkSpeed, Is.EqualTo(2 * normalSpeed.BaseWalkSpeed));
            Assert.That(babySpeed.BaseSprintSpeed, Is.EqualTo(2 * normalSpeed.BaseSprintSpeed));
            Assert.That(entities.GetComponent<MeleeWeaponComponent>(baby).Damage.GetTotal().Float(),
                Is.EqualTo(entities.GetComponent<MeleeWeaponComponent>(normal).Damage.GetTotal().Float() / 2));
            var babyDeath = entities.GetComponent<MobThresholdsComponent>(baby).Thresholds.Single(e => e.Value == MobState.Dead).Key;
            var normalDeath = entities.GetComponent<MobThresholdsComponent>(normal).Thresholds.Single(e => e.Value == MobState.Dead).Key;
            Assert.That(babyDeath.Float(), Is.EqualTo(normalDeath.Float() / 2));
        });
        await pair.CleanReturnAsync();
    }
}
