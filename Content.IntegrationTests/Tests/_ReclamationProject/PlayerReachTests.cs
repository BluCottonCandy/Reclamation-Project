using System;
using System.Numerics;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class PlayerReachTests
{
    [TestCase(.9f, true)]
    [TestCase(1.15f, false)]
    [TestCase(1.8f, false)]
    public async Task PlayerInteractionsUseCenterDistance(float distance, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var item = em.SpawnEntity("N14CombatKnife", map.GridCoords.Offset(new Vector2(distance, 0)));
            var target = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(distance, 0)));
            pair.Server.PlayerMan.SetAttachedEntity(session, user);
            var interaction = em.System<SharedInteractionSystem>();
            Assert.That(interaction.InRangeUnobstructed(user, item), Is.EqualTo(expected), "Item reach");
            Assert.That(interaction.InRangeUnobstructed(user, target), Is.EqualTo(expected), "Body reach");
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
            Assert.That(interaction.InRangeUnobstructed(user, target), Is.True, "NPC reach stays unchanged.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("N14CombatKnife", 1.4f, false, true)]
    [TestCase("N14CombatKnife", 1.6f, false, false)]
    [TestCase("N14CombatKnife", 1.4f, true, true)]
    [TestCase("N14CombatKnife", 1.6f, true, false)]
    [TestCase("N14WastelandTribalSpear", 1.9f, false, true)]
    [TestCase("N14WastelandTribalSpear", 2.1f, false, false)]
    [TestCase("N14WastelandTribalSpear", 1.9f, true, true)]
    [TestCase("N14WastelandTribalSpear", 2.1f, true, false)]
    public async Task PlayerMeleeMatchesReachForClicksAndWideSwings(string prototype, float distance, bool wide, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid user = default, target = default, weapon = default;
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = -2; x <= 4; x++)
            for (var y = -2; y <= 2; y++)
                em.System<SharedMapSystem>().SetTile(map.Grid, map.GridCoords.Offset(new Vector2(x,y)), map.Tile.Tile);
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            target = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(distance,0)));
            weapon = em.SpawnEntity(prototype, map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, weapon), Is.True);
            em.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            pair.Server.PlayerMan.SetAttachedEntity(session, user);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            em.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            var system = em.System<Content.Server.Weapons.Melee.MeleeWeaponSystem>();
            var melee = em.GetComponent<MeleeWeaponComponent>(weapon);
            melee.NextAttack = TimeSpan.Zero;
            var attacked = wide ? system.AttemptWideAttack(user, weapon, melee, target)
                : system.AttemptLightAttack(user, weapon, melee, target);
            Assert.That(attacked, Is.True, "Attack must not be blocked by equip cooldown or combat state.");
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage > 0, Is.EqualTo(expected));
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
        });
        await pair.CleanReturnAsync();
    }
}
