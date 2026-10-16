using System;
using System.Linq;
using System.Numerics;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat.Melee;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Blocking;
using Content.Shared.Hands.EntitySystems;
using Content.Server.Destructible;
using Content.Shared.Throwing;
using Content.Server._ReclamationProject.Mobs;
using Robust.Shared.Map;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
public sealed class MirelurkQueenWideSwingTest
{
    [Test]
    public async Task QueenDamagesRaisedShieldWithClawsAndEnragedCharge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var position = map.GridCoords.Offset(new Vector2(2.5f, 0));
            entities.System<SharedMapSystem>().SetTile(map.Grid, position, map.Tile.Tile);
            var target = entities.SpawnEntity("MobHuman", position);
            var shield = entities.SpawnEntity("RiotShield", position);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(target, shield), Is.True);
            entities.RemoveComponent<DestructibleComponent>(shield);
            var blocking = entities.GetComponent<BlockingComponent>(shield);
            blocking.ActiveBlockFraction = 1;
            blocking.ActiveBlockDamageModifier = new DamageModifierSet();
            Assert.That(entities.System<BlockingSystem>().StartBlocking(shield, blocking, target), Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(target).Anchored, Is.True);
            var attack = entities.EnsureComponent<NPCMeleeCombatComponent>(queen);
            attack.Target = target;
            attack.MissChance = 0;
            entities.System<NPCSystem>().WakeNPC(queen);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(queen, true);
            entities.GetComponent<MeleeWeaponComponent>(queen).NextAttack = default;
            entities.System<NPCCombatSystem>().Update(0);
            Assert.That(entities.GetComponent<DamageableComponent>(shield).TotalDamage.Float(), Is.EqualTo(50));
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.Zero);
            entities.GetComponent<StaminaComponent>(target).CritThreshold = 1000;
            var charge = entities.GetComponent<MirelurkQueenChargeComponent>(queen);
            charge.Enraged = true;
            charge.Dashing = true;
            charge.WindingUp = false;
            charge.DashDirection = Vector2.UnitX;
            var thrown = entities.EnsureComponent<ThrownItemComponent>(queen);
            entities.EventBus.RaiseLocalEvent(queen, new ThrowDoHitEvent(queen, target, thrown, null), true);
            Assert.That(entities.GetComponent<DamageableComponent>(shield).TotalDamage.Float(), Is.EqualTo(80));
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QueenHitsStationaryAnchoredMob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var position = map.GridCoords.Offset(new Vector2(2.5f, 0));
            entities.System<SharedMapSystem>().SetTile(map.Grid, position, map.Tile.Tile);
            var target = entities.SpawnEntity("MobHuman", position);
            entities.System<SharedTransformSystem>().AnchorEntity(target);
            Assert.That(entities.GetComponent<TransformComponent>(target).Anchored, Is.True);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(queen, true);
            entities.GetComponent<MeleeWeaponComponent>(queen).NextAttack = default;
            Assert.That(entities.System<SharedMeleeWeaponSystem>().AttemptWideAttack(queen, queen,
                entities.GetComponent<MeleeWeaponComponent>(queen), target), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(50));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QueenSweepsMultipleTargetsAtDoubleReach()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var primary = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0)));
            var secondary = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0.6f)));
            var behind = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-2.5f, 0)));
            var far = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(4f, 0)));
            var htn = entities.GetComponent<HTNComponent>(queen);
            Assert.That(htn.RootTask.Task, Is.EqualTo("ReclamationMirelurkQueenCombatCompound"));
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var pursuit = prototypes.Index<HTNCompoundPrototype>("ReclamationMirelurkQueenAttackTargetCompound");
            var steps = pursuit.Branches.SelectMany(branch => branch.Tasks).OfType<HTNPrimitiveTask>().ToArray();
            Assert.That(steps.Any(step => step.Operator is MoveToOperator), Is.True);
            Assert.That(steps.Any(step => step.Operator is MeleeOperator), Is.True);
            Assert.That(steps.Any(step => step.Operator is JukeOperator), Is.False);
            var blackboard = htn.Blackboard;
            Assert.That(blackboard.GetValue<bool>("MeleeWideAttack"), Is.True);
            Assert.That(blackboard.GetValue<float>(NPCBlackboard.MeleeMissChance), Is.Zero);
            var attack = entities.EnsureComponent<NPCMeleeCombatComponent>(queen);
            attack.Target = primary;
            attack.MissChance = blackboard.GetValue<float>(NPCBlackboard.MeleeMissChance);
            entities.System<NPCSystem>().WakeNPC(queen);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(queen, true);
            entities.GetComponent<MeleeWeaponComponent>(queen).NextAttack = default;

            // Exercise the AI attack path, not just a direct weapon-system call.
            entities.System<NPCCombatSystem>().Update(0);
            var weaponTiming = entities.GetComponent<MeleeWeaponComponent>(queen);
            var now = pair.Server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That((weaponTiming.NextAttack - now).TotalSeconds, Is.EqualTo(2.0 / 3.0).Within(0.001));
            entities.System<NPCCombatSystem>().Update(0); // No second hit before the cooldown expires.
            Assert.That(entities.GetComponent<DamageableComponent>(primary).TotalDamage.Float(), Is.EqualTo(50));
            Assert.That(entities.GetComponent<DamageableComponent>(secondary).TotalDamage.Float(), Is.EqualTo(50));
            Assert.That(entities.GetComponent<DamageableComponent>(behind).TotalDamage.Float(), Is.Zero);
            Assert.That(entities.GetComponent<DamageableComponent>(far).TotalDamage.Float(), Is.Zero);

            var normal = entities.SpawnEntity("N14MobMirelurk", map.GridCoords);
            var baby = entities.SpawnEntity("ReclamationMobBabyMirelurk", map.GridCoords);
            foreach (var other in new[] { normal, baby })
            {
                var otherBoard = entities.GetComponent<HTNComponent>(other).Blackboard;
                Assert.That(otherBoard.TryGetValue<bool>("MeleeWideAttack", out var wide, entities) && wide, Is.False);
                Assert.That(otherBoard.GetValueOrDefault<float>(NPCBlackboard.MeleeMissChance, entities), Is.EqualTo(0.3f));
                var weapon = entities.GetComponent<MeleeWeaponComponent>(other);
                Assert.That(weapon.Range * weapon.HeavyRangeModifier, Is.EqualTo(1.5f));
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QueenCannotSweepThroughBrickWalls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var queen = entities.SpawnEntity("ReclamationMobMirelurkQueen", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0)));
            entities.SpawnEntity("N14WallBrickSlantedIndestructible", map.GridCoords.Offset(new Vector2(1, 0)));
            entities.System<SharedCombatModeSystem>().SetInCombatMode(queen, true);
            var weapon = entities.GetComponent<MeleeWeaponComponent>(queen);
            weapon.NextAttack = default;
            entities.System<SharedMeleeWeaponSystem>().AttemptWideAttack(queen, queen, weapon, target);
            Assert.That(entities.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }
}
