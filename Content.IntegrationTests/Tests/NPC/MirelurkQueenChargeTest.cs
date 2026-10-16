using System;
using Content.Shared.Damage.Systems;
using Content.Shared.Damage.Prototypes;
using Content.Server.Body.Components;
using Robust.Shared.Prototypes;
using System.Numerics;
using Content.Server._ReclamationProject.Mobs;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Misfits.MeleeCharge;
using Content.Shared._Misfits.NPC;
using Content.Shared.Weapons.Melee;
using Content.Shared.Damage.Components;
using Content.Shared.Damage;
using Robust.Shared.Physics.Components;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
public sealed class MirelurkQueenChargeTest
{
    [Test]
    public async Task EnrageTriggersOnceAndChargesImmediately()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0)));
            var comp = entities.GetComponent<MirelurkQueenChargeComponent>(queen);
            var damage = entities.System<DamageableSystem>();
            var blunt = prototypes.Index<DamageTypePrototype>("Blunt");
            var regeneration = entities.GetComponent<PassiveDamageComponent>(queen);
            var normalHealingInterval = regeneration.Interval;
            damage.TryChangeDamage(queen, new DamageSpecifier(blunt, 998));
            Assert.That(comp.Enraged, Is.False);
            damage.TryChangeDamage(queen, new DamageSpecifier(blunt, 2));
            Assert.That(comp.Enraged, Is.True);
            Assert.That(regeneration.Interval, Is.EqualTo(normalHealingInterval / 1.4f).Within(0.0001f));
            Assert.That(comp.StaminaDamage, Is.EqualTo(120).Within(0.001));
            Assert.That(entities.GetComponent<MeleeWeaponComponent>(queen).Damage.DamageDict["Blunt"].Float(), Is.EqualTo(24));
            Assert.That(entities.GetComponent<MeleeWeaponComponent>(queen).Damage.GetTotal().Float(), Is.EqualTo(60));
            var chemicals = entities.GetComponent<BloodstreamComponent>(queen).ChemicalSolution!.Value.Comp.Solution;
            Assert.That(chemicals.GetTotalPrototypeQuantity("Omnizine").Float(), Is.Zero);
            damage.TryChangeDamage(queen, new DamageSpecifier(blunt, -1000));
            damage.TryChangeDamage(queen, new DamageSpecifier(blunt, 1000));
            Assert.That(chemicals.GetTotalPrototypeQuantity("Omnizine").Float(), Is.Zero);
            Assert.That(regeneration.Interval, Is.EqualTo(normalHealingInterval / 1.4f).Within(0.0001f));
            Assert.That(entities.GetComponent<MeleeWeaponComponent>(queen).Damage.GetTotal().Float(), Is.EqualTo(60));
            Assert.That(entities.System<MirelurkQueenChargeSystem>().TryCharge((queen, comp), target), Is.True);
            Assert.That(comp.WindingUp, Is.False);
            Assert.That(comp.Dashing, Is.True);
            Assert.That(comp.NextCharge, Is.EqualTo(pair.Server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(2)));
            var thrown = entities.GetComponent<ThrownItemComponent>(queen);
            entities.EventBus.RaiseLocalEvent(queen, new ThrowDoHitEvent(queen, target, thrown, null), true);
            entities.EventBus.RaiseLocalEvent(queen, new ThrowDoHitEvent(queen, target, thrown, null), true);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(30));

        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeathDuringWindupCancelsCharge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid queen = default;
        await pair.Server.WaitAssertion(() =>
        {
            queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0)));
            var charge = entities.GetComponent<MirelurkQueenChargeComponent>(queen);
            var system = entities.System<MirelurkQueenChargeSystem>();
            Assert.That(system.TryCharge((queen, charge), target), Is.True);
            Assert.That(charge.WindingUp, Is.True);
            entities.System<MobStateSystem>().ChangeMobState(queen, MobState.Dead);
            system.Update(0);
            Assert.That(charge.WindingUp, Is.False);
            Assert.That(entities.HasComponent<ThrownItemComponent>(queen), Is.False);
            Assert.That(entities.GetComponent<StaminaComponent>(target).StaminaDamage, Is.Zero);
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<MeleeChargeComponent>(queen), Is.False));
        await pair.CleanReturnAsync();
    }

    [TestCase(1.1f)]
    [TestCase(1.5f)]
    [TestCase(2.5f)]
    [TestCase(3.4f)]
    public async Task ChargingIntoPlayerAppliesStaminaDamageInPhysics(float distance)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid queen = default;
        EntityUid target = default;
        await pair.Server.WaitAssertion(() =>
        {
            var gravity = entities.EnsureComponent<GravityComponent>(map.Grid);
            gravity.Inherent = true;
            gravity.EnabledVV = true;
            var maps = entities.System<SharedMapSystem>();
            for (var x = -1; x <= 4; x++)
            for (var y = -1; y <= 1; y++)
                maps.SetTile(map.Grid, map.GridCoords.Offset(new Vector2(x, y)), map.Tile.Tile);
            queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            entities.RemoveComponent<ProximityNPCComponent>(queen); // No connected players in this isolated physics test.
            entities.GetComponent<MeleeWeaponComponent>(queen).NextAttack = TimeSpan.MaxValue; // Measure charge damage alone.
            target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(distance, 0)));
            entities.EnsureComponent<NPCMeleeCombatComponent>(queen).Target = target;
            entities.System<NPCSystem>().WakeNPC(queen);
            var charge = entities.GetComponent<MirelurkQueenChargeComponent>(queen);
            Assert.That(entities.System<MirelurkQueenChargeSystem>().TryCharge((queen, charge), target), Is.True);
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<MirelurkQueenChargeComponent>(queen).WindingUp, Is.True);
            Assert.That(entities.HasComponent<ThrownItemComponent>(queen), Is.False);
            Assert.That(entities.GetComponent<StaminaComponent>(target).StaminaDamage, Is.Zero);
            Assert.That(entities.GetComponent<PhysicsComponent>(queen).LinearVelocity, Is.EqualTo(Vector2.Zero));
        });
        await pair.RunTicksSync(70);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<StaminaComponent>(target).StaminaDamage, Is.EqualTo(100),
                $"Queen position: {entities.System<SharedTransformSystem>().GetWorldPosition(queen)}; target: {entities.System<SharedTransformSystem>().GetWorldPosition(target)}; impact: {entities.GetComponent<MirelurkQueenChargeComponent>(queen).ImpactApplied}");
            Assert.That(entities.HasComponent<MeleeChargeComponent>(queen), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AiChargeIsFreeWithFourSecondCooldownAndSingleImpact()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        await pair.Server.WaitAssertion(() =>
        {
            var queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0)));
            var other = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 1)));
            var behind = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-2f, 0)));
            var sheltered = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.8f, -1)));
            entities.SpawnEntity("N14WallBrickSlantedIndestructible", map.GridCoords.Offset(new Vector2(2, -1)));
            var ally = entities.SpawnEntity("N14MobMirelurk", map.GridCoords.Offset(new Vector2(1.5f, 0.2f)));
            entities.EnsureComponent<StaminaComponent>(ally);
            var targetStamina = entities.GetComponent<StaminaComponent>(target);
            targetStamina.CritThreshold = 500; // Detect repeated damage instead of stopping at stamina crit.
            var queenStamina = entities.EnsureComponent<StaminaComponent>(queen);
            var initialStamina = queenStamina.StaminaDamage;
            entities.EnsureComponent<NPCMeleeCombatComponent>(queen).Target = target;
            entities.System<NPCSystem>().WakeNPC(queen);
            var system = entities.System<MirelurkQueenChargeSystem>();
            var charge = entities.GetComponent<MirelurkQueenChargeComponent>(queen);
            system.Update(0);
            Assert.That(entities.HasComponent<MeleeChargeComponent>(queen), Is.True);
            Assert.That(charge.WindingUp, Is.True);
            Assert.That(charge.WindupEnd - timing.CurTime, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(entities.HasComponent<ThrownItemComponent>(queen), Is.False);
            Assert.That(queenStamina.StaminaDamage, Is.EqualTo(initialStamina));
            Assert.That(charge.NextCharge - timing.CurTime, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(system.TryCharge((queen, charge), target), Is.False);

            charge.WindupEnd = timing.CurTime;
            system.Update(0);
            Assert.That(charge.WindingUp, Is.False);
            var thrown = entities.GetComponent<ThrownItemComponent>(queen);
            entities.EventBus.RaiseLocalEvent(queen, new ThrowDoHitEvent(queen, target, thrown, null), true);
            entities.EventBus.RaiseLocalEvent(queen, new ThrowDoHitEvent(queen, target, thrown, null), true);
            entities.EventBus.RaiseLocalEvent(queen, new ThrowDoHitEvent(queen, other, thrown, null), true);
            Assert.That(targetStamina.StaminaDamage, Is.EqualTo(100));
            Assert.That(entities.GetComponent<StaminaComponent>(other).StaminaDamage, Is.EqualTo(100));
            Assert.That(entities.GetComponent<StaminaComponent>(behind).StaminaDamage, Is.Zero);
            Assert.That(entities.GetComponent<StaminaComponent>(sheltered).StaminaDamage, Is.Zero);
            Assert.That(entities.GetComponent<StaminaComponent>(ally).StaminaDamage, Is.Zero);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.Zero);
            system.Update(0); // End the dash outside the physics collision callback.
            Assert.That(entities.HasComponent<ThrownItemComponent>(queen), Is.False);
            Assert.That(queenStamina.StaminaDamage, Is.EqualTo(initialStamina));

            entities.System<MobStateSystem>().ChangeMobState(queen, MobState.Dead);
            charge.NextCharge = default;
            Assert.That(system.TryCharge((queen, charge), target), Is.False);
            foreach (var prototype in new[] { "N14MobMirelurk", "ReclamationMobBabyMirelurk" })
            {
                var ordinary = entities.SpawnEntity(prototype, map.GridCoords);
                Assert.That(entities.HasComponent<MirelurkQueenChargeComponent>(ordinary), Is.False);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChargeCannotStartThroughWalls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0)));
            entities.SpawnEntity("N14WallBrickSlantedIndestructible", map.GridCoords.Offset(new Vector2(1, 0)));
            var charge = entities.GetComponent<MirelurkQueenChargeComponent>(queen);
            Assert.That(entities.System<MirelurkQueenChargeSystem>().TryCharge((queen, charge), target), Is.False);
            Assert.That(entities.HasComponent<MeleeChargeComponent>(queen), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
