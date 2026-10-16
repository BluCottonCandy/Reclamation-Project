using System.Numerics;
using Content.Shared.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Misfits.NPC;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
public sealed class SniperRetaliationTest
{
    [TestCase("N14MobGhoulFeral")]
    [TestCase("N14MobRaiderPsycho")]
    [TestCase("N14MobSuperMutantNPC")]
    public async Task DistantHitWakesEnemyAndSelectsShooter(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var enemy = entities.SpawnEntity(prototype, map.GridCoords);
            var shooter = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(40, 0)));
            entities.System<NpcFactionSystem>().AddFaction((shooter, null), "Wastelander");
            var htn = entities.GetComponent<HTNComponent>(enemy);
            var utility = entities.System<NPCUtilitySystem>();
            var before = utility.GetEntities(htn.Blackboard, "NearbyMeleeTargets", false).Entities;
            Assert.That(before.ContainsKey(shooter), Is.False, "An unprovoked NPC should not have long-range sight");
            entities.System<NPCSystem>().SleepNPC(enemy);
            Assert.That(entities.HasComponent<ActiveNPCComponent>(enemy), Is.False);

            var damage = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Piercing"), 5);
            entities.System<DamageableSystem>().TryChangeDamage(enemy, damage, origin: shooter);

            Assert.That(entities.HasComponent<ActiveNPCComponent>(enemy), Is.True, "A shot did not wake the NPC");
            var retaliation = entities.System<NPCRetaliationSystem>();
            Assert.That(retaliation.TryGetPursuitTarget(enemy, out var attacker), Is.True);
            Assert.That(attacker, Is.EqualTo(shooter));
            Assert.That(htn.Blackboard.GetValue<EntityUid>("Target"), Is.EqualTo(shooter));
            var after = utility.GetEntities(htn.Blackboard, "NearbyMeleeTargets", false).Entities;
            Assert.That(after.ContainsKey(shooter), Is.True, "The shooter was discarded beyond ordinary sight");
            Assert.That(retaliation.GetTargetRange(enemy, shooter, 14), Is.GreaterThan(40));
            var bystander = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(42, 0)));
            Assert.That(retaliation.GetTargetRange(enemy, bystander, 14), Is.EqualTo(14), "Pursuit must not expand all passive sight");
            entities.DeleteEntity(bystander);
            entities.DeleteEntity(shooter);
            entities.DeleteEntity(enemy);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LongRangePursuitExpires()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid enemy = default;
        EntityUid shooter = default;
        await pair.Server.WaitAssertion(() =>
        {
            enemy = entities.SpawnEntity("N14MobGhoulFeral", map.GridCoords);
            shooter = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(40, 0)));
            var retaliation = entities.System<NPCRetaliationSystem>();
            Assert.That(retaliation.TryRetaliate((enemy, entities.GetComponent<NPCRetaliationComponent>(enemy)), shooter), Is.True);
            Assert.That(retaliation.TryGetPursuitTarget(enemy, out _), Is.True);
            entities.RemoveComponent<ProximityNPCComponent>(enemy);
            entities.System<NPCSystem>().SleepNPC(enemy);
        });
        await pair.RunSeconds(31);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.System<NPCRetaliationSystem>().TryGetPursuitTarget(enemy, out _), Is.False);
            entities.DeleteEntity(shooter);
            entities.DeleteEntity(enemy);
        });
        await pair.CleanReturnAsync();
    }
}
