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
using Content.Shared.Damage;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
public sealed class MirelurkQueenWideSwingTest
{
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
