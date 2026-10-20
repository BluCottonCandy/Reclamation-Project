using System;
using System.Linq;
using System.Collections.Generic;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Alert;
using Content.Server.Weapons.Melee;
using Content.Shared.CombatMode;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using Content.Shared.Blocking;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Server.Repairable;
using System.Numerics;
using Content.Server._ReclamationProject.Shields;
using Content.Shared._ReclamationProject.Shields;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Explosion;
using Content.Shared.Gravity;
using Robust.Shared.Map;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Clothing;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class PaladinShieldTest
{
    [TestCase("ReclamationPaladinShield")]
    [TestCase("ReclamationPaladinGreatshield")]
    public async Task RaisedShieldAbsorbsFullDamageAndReleasesOnBreak(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var shield = entities.SpawnEntity(prototype, map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, shield), Is.True);
            var blocking = entities.GetComponent<BlockingComponent>(shield);
            Assert.That(blocking.BlockingToggleActionEntity, Is.Not.Null);
            var system = entities.System<BlockingSystem>();
            Assert.That(system.StartBlocking(shield, blocking, user), Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(user).Anchored, Is.True);
            entities.System<StaminaSystem>().TakeStaminaDamage(user, 40);
            Assert.That(entities.GetComponent<StaminaComponent>(user).StaminaDamage, Is.Zero,
                "Raised blocking retains full stamina protection.");
            Assert.That(entities.System<PaladinShieldSystem>().TryCharge(user, shield, map.GridCoords.Offset(Vector2.UnitX)), Is.False);
            var damage = entities.System<DamageableSystem>();
            damage.TryChangeDamage(user, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 100));
            Assert.That(entities.GetComponent<DamageableComponent>(user).TotalDamage.Float(), Is.Zero);
            Assert.That(entities.GetComponent<DamageableComponent>(shield).TotalDamage.Float(), Is.EqualTo(100));
            var blastDamage = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 50);
            var blast = new BeforeExplodeEvent(blastDamage, "Default", new List<EntityUid>());
            entities.EventBus.RaiseLocalEvent(user, ref blast);
            Assert.That(blastDamage.GetTotal().Float(), Is.Zero);
            Assert.That(entities.GetComponent<DamageableComponent>(shield).TotalDamage.Float(), Is.EqualTo(150));
            system.StopBlocking(shield, blocking, user);
            Assert.That(entities.GetComponent<TransformComponent>(user).Anchored, Is.False);
            Assert.That(system.StartBlocking(shield, blocking, user), Is.True);
            damage.TryChangeDamage(user, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 1000));
            Assert.That(entities.GetComponent<DamageableComponent>(user).TotalDamage.Float(), Is.Zero);
            entities.System<PaladinShieldSystem>().Update(0);
            Assert.That(entities.GetComponent<TransformComponent>(user).Anchored, Is.False);
            var broken = entities.System<SharedHandsSystem>().EnumerateHeld(user)
                .Single(item => entities.HasComponent<PaladinShieldComponent>(item));
            Assert.That(entities.GetComponent<PaladinShieldComponent>(broken).Damaged, Is.True);
            Assert.That(system.StartBlocking(broken, entities.GetComponent<BlockingComponent>(broken), user), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ReclamationPaladinShield", "ReclamationDamagedPaladinShield", 250, 65)]
    [TestCase("ReclamationPaladinGreatshield", "ReclamationDamagedPaladinGreatshield", 500, 50)]
    public async Task ShieldWearBreakHudAndThirtySecondWeldRepair(string prototype, string damagedPrototype, int hp, int wear)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        EntityUid holder = default;
        EntityUid damaged = default;
        await pair.Server.WaitAssertion(() =>
        {
            holder = entities.SpawnEntity("MobHuman", map.GridCoords);
            var shield = entities.SpawnEntity(prototype, map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(holder, shield), Is.True);
            var hands = entities.GetComponent<HandsComponent>(holder);
            Assert.That(hands.CountFreeHands(), Is.EqualTo(prototype == "ReclamationPaladinGreatshield" ? 0 : 1));
            var alerts = entities.System<AlertsSystem>();
            Assert.That(alerts.IsShowingAlert(holder, "ReclamationShieldIntegrity"), Is.True);
            var blunt = prototypes.Index<DamageTypePrototype>("Blunt");
            var damage = entities.System<DamageableSystem>();
            damage.TryChangeDamage(holder, new DamageSpecifier(blunt, 100));
            Assert.That(entities.GetComponent<DamageableComponent>(shield).TotalDamage.Float(), Is.EqualTo(wear));
            Assert.That(entities.GetComponent<PowerArmorIntegrityComponent>(shield).MaxIntegrity.Float(), Is.EqualTo(hp));
            damage.TryChangeDamage(shield, new DamageSpecifier(blunt, hp), ignoreResistances: true);
            entities.System<PaladinShieldSystem>().Update(0);
            damaged = entities.System<SharedHandsSystem>().EnumerateHeld(holder)
                .Single(item => entities.HasComponent<PaladinShieldComponent>(item));
            Assert.That(entities.GetComponent<MetaDataComponent>(damaged).EntityPrototype!.ID, Is.EqualTo(damagedPrototype));
            Assert.That(entities.GetComponent<DamageableComponent>(damaged).TotalDamage.Float(), Is.EqualTo(hp));
            Assert.That(entities.System<PaladinShieldProtectionSystem>().GetProtection(holder), Is.EqualTo(1));
            Assert.That(alerts.GetActiveAlerts(holder)!.Values.Single(a => a.Type == "ReclamationShieldIntegrity").Severity, Is.EqualTo(0));
            entities.EnsureComponent<GodmodeComponent>(holder);
            var repairer = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0, 1f)));
            entities.EnsureComponent<GodmodeComponent>(repairer);
            var welder = entities.SpawnEntity("Welder", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(repairer, welder), Is.True);
            Assert.That(entities.System<ItemToggleSystem>().TryActivate((welder, null), repairer), Is.True);
            Assert.That(entities.GetComponent<RepairableComponent>(damaged).DoAfterDelay, Is.EqualTo(30));
            var interaction = new InteractUsingEvent(repairer, welder, damaged, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(damaged, interaction);
            Assert.That(interaction.Handled, Is.True);
        });
        await pair.RunTicksSync(pair.Server.ResolveDependency<IGameTiming>().TickRate * 29);
        await pair.Server.WaitAssertion(() => Assert.That(entities.EntityExists(damaged), Is.True, "A full weld repair must not finish before 30 seconds."));
        await pair.RunTicksSync(pair.Server.ResolveDependency<IGameTiming>().TickRate * 2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(damaged), Is.False);
            var restored = entities.System<SharedHandsSystem>().EnumerateHeld(holder)
                .Single(item => entities.HasComponent<PaladinShieldComponent>(item));
            Assert.That(entities.GetComponent<MetaDataComponent>(restored).EntityPrototype!.ID, Is.EqualTo(prototype));
            Assert.That(entities.GetComponent<DamageableComponent>(restored).TotalDamage.Float(), Is.Zero);
            Assert.That(entities.System<AlertsSystem>().GetActiveAlerts(holder)!.Values.Single(a => a.Type == "ReclamationShieldIntegrity").Severity, Is.EqualTo(4));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(1.1f)]
    [TestCase(3.4f)]
    public async Task GreatshieldChargeHitsThroughActualCollision(float distance)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default;
        EntityUid target = default;
        await pair.Server.WaitAssertion(() =>
        {
            var gravity = entities.EnsureComponent<GravityComponent>(map.Grid);
            gravity.EnabledVV = true;
            gravity.Inherent = true;
            for (var x = -1; x <= 4; x++)
            for (var y = -1; y <= 1; y++)
                entities.System<SharedMapSystem>().SetTile(map.Grid, map.GridCoords.Offset(new Vector2(x, y)), map.Tile.Tile);
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(distance, 0)));
            var shield = entities.SpawnEntity("ReclamationPaladinGreatshield", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, shield), Is.True);
            Assert.That(entities.GetComponent<PaladinShieldComponent>(shield).ChargeAction, Is.Not.Null);
            Assert.That(entities.System<PaladinShieldSystem>().TryCharge(user, shield, entities.GetComponent<TransformComponent>(target).Coordinates), Is.True);
        });
        await pair.RunTicksSync(70);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(60).Within(0.1));
            Assert.That(entities.GetComponent<StaminaComponent>(target).StaminaDamage, Is.EqualTo(50));
            Assert.That(entities.GetComponent<ShieldChargeComponent>(user).Active, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ReclamationPaladinShield", 0.7f, 40, 50, 10)]
    [TestCase("ReclamationPaladinGreatshield", 0.6f, 60, 70, 50)]
    public async Task HeldProtectionChargeAndCooldown(string prototype, float coefficient, int chargeDamage, int meleeDamage, int staminaDamage)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0)));
            entities.GetComponent<StaminaComponent>(target).CritThreshold = 500;
            var other = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 1)));
            var behind = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-2, 0)));
            var shield = entities.SpawnEntity(prototype, map.GridCoords);
            var hands = entities.System<SharedHandsSystem>();
            Assert.That(hands.TryPickup(user, shield), Is.True);
            Assert.That(entities.GetComponent<MeleeWeaponComponent>(shield).Damage.GetTotal().Float(), Is.EqualTo(meleeDamage));
            Assert.That(entities.GetComponent<MeleeWeaponComponent>(shield).AttackRate, Is.EqualTo(2f));
            Assert.That(entities.System<PaladinShieldProtectionSystem>().GetProtection(user), Is.EqualTo(coefficient));
            var damage = entities.System<DamageableSystem>();
            foreach (var type in new[] { "Blunt", "Slash", "Piercing" })
            {
                var result = damage.TryChangeDamage(user, new DamageSpecifier(prototypes.Index<DamageTypePrototype>(type), 10));
                Assert.That(result!.GetTotal().Float(), Is.EqualTo(10 * coefficient).Within(0.01f));
            }
            var heat = damage.TryChangeDamage(user, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Heat"), 10));
            Assert.That(heat!.GetTotal().Float(), Is.EqualTo(10));
            var explosion = new GetExplosionResistanceEvent("Default");
            entities.EventBus.RaiseLocalEvent(user, ref explosion);
            Assert.That(explosion.DamageCoefficient, Is.EqualTo(coefficient));
            var previousWear = entities.GetComponent<DamageableComponent>(shield).TotalDamage.Float();
            var blast = new BeforeExplodeEvent(new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 100 * coefficient), "Default", new List<EntityUid>());
            entities.EventBus.RaiseLocalEvent(user, ref blast);
            Assert.That(blast.Contents.Contains(shield), Is.False, "Explosion recursion must not damage the protecting shield twice.");
            Assert.That(entities.GetComponent<DamageableComponent>(shield).TotalDamage.Float() - previousWear,
                Is.EqualTo(100 * entities.GetComponent<PaladinShieldComponent>(shield).WearFraction).Within(0.1));

            if (prototype == "ReclamationPaladinGreatshield")
            {
                var speed = entities.GetComponent<ClothingSpeedModifierComponent>(shield);
                Assert.That(speed.WalkModifier, Is.EqualTo(0.75f));
                Assert.That(speed.SprintModifier, Is.EqualTo(0.75f));
            }
            var stamina = entities.GetComponent<StaminaComponent>(user);
            stamina.CritThreshold = 500;
            var previousStamina = stamina.StaminaDamage;
            entities.System<StaminaSystem>().TakeStaminaDamage(user, 100);
            Assert.That(stamina.StaminaDamage - previousStamina,
                Is.EqualTo(prototype == "ReclamationPaladinGreatshield" ? 75 : 100));
            var system = entities.System<PaladinShieldSystem>();
            Assert.That(system.TryCharge(user, shield, entities.GetComponent<TransformComponent>(target).Coordinates), Is.True);
            var state = entities.GetComponent<ShieldChargeComponent>(user);
            Assert.That(state.NextCharge, Is.EqualTo(pair.Server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(4)));
            var thrown = entities.GetComponent<ThrownItemComponent>(user);
            entities.EventBus.RaiseLocalEvent(user, new ThrowDoHitEvent(user, target, thrown, null), true);
            entities.EventBus.RaiseLocalEvent(user, new ThrowDoHitEvent(user, target, thrown, null), true);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(chargeDamage));
            Assert.That(entities.GetComponent<StaminaComponent>(target).StaminaDamage, Is.EqualTo(staminaDamage));
            Assert.That(entities.GetComponent<DamageableComponent>(other).TotalDamage.Float(), Is.EqualTo(chargeDamage));
            Assert.That(entities.GetComponent<DamageableComponent>(behind).TotalDamage.Float(), Is.Zero);
            system.Update(0);
            Assert.That(system.TryCharge(user, shield, entities.GetComponent<TransformComponent>(target).Coordinates), Is.False);
        });
        await pair.CleanReturnAsync();
    }
    [TestCase("ReclamationPaladinGreatshield", 25, 75)]
    [TestCase("ReclamationPaladinShield", 0, 100)]
    [TestCase("ReclamationDamagedPaladinGreatshield", 0, 100)]
    [TestCase("ReclamationDamagedPaladinShield", 0, 100)]
    public async Task StaminaProtectionPanelMatchesActualDamage(string prototype, int protection, int expectedDamage)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var shield = em.SpawnEntity(prototype, map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, shield), Is.True);
            Assert.That(em.GetComponent<MeleeWeaponComponent>(shield).AttackRate, Is.EqualTo(2f));
            em.GetComponent<StaminaComponent>(user).CritThreshold = 500;
            em.System<StaminaSystem>().TakeStaminaDamage(user, 100);
            Assert.That(em.GetComponent<StaminaComponent>(user).StaminaDamage, Is.EqualTo(expectedDamage));

            // Capture the same detailed-examine message used by the Protection button.
            var group = new ExamineGroup { Components = new() { "Blocking" } };
            em.EnsureComponent<GroupExamineComponent>(shield).Group = new() { group };
            var ev = new GetVerbsEvent<ExamineVerb>(user, shield, null, null, true, true, true, new());
            em.EventBus.RaiseLocalEvent(shield, ev, true);
            var message = group.Entries.Single(entry => entry.Component == "Blocking").Message.ToString();
            Assert.That(message, Does.Contain($"Stamina protection: {protection}%"));
            em.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ReclamationPaladinShield", false)]
    [TestCase("ReclamationPaladinShield", true)]
    [TestCase("ReclamationPaladinGreatshield", false)]
    [TestCase("ReclamationPaladinGreatshield", true)]
    public async Task RealSwingsTakeTwiceTheOriginalCooldown(string prototype, bool wide)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var target = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(.8f, 0)));
            em.EnsureComponent<GodmodeComponent>(target);
            var shield = em.SpawnEntity(prototype, map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, shield), Is.True);
            em.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            var weapon = em.GetComponent<MeleeWeaponComponent>(shield);
            var configuredInterval = weapon.AttackRate;
            var melee = em.System<MeleeWeaponSystem>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            bool Swing() => wide ? melee.AttemptWideAttack(user, shield, weapon, target)
                : melee.AttemptLightAttack(user, shield, weapon, target);

            // Exercise the real attack scheduling at the former one-second setting.
            weapon.AttackRate = 1f;
            weapon.NextAttack = TimeSpan.Zero;
            Assert.That(Swing(), Is.True);
            var formerCooldown = weapon.NextAttack - timing.CurTime;
            Assert.That(formerCooldown, Is.GreaterThan(TimeSpan.Zero));

            weapon.AttackRate = configuredInterval;
            weapon.NextAttack = TimeSpan.Zero;
            Assert.That(Swing(), Is.True);
            var cooldown = weapon.NextAttack - timing.CurTime;
            Assert.That(cooldown.TotalSeconds, Is.EqualTo(formerCooldown.TotalSeconds * 2).Within(.001));
            Assert.That(cooldown.TotalSeconds, Is.EqualTo(wide ? 2.4 : 2).Within(.001));
            Assert.That(Swing(), Is.False, "A second swing must be rejected until the cooldown expires.");
            em.DeleteEntity(user);
            em.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }
}
