using System.Numerics;
using Content.Server.Weapons.Melee;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Shared._ReclamationProject.GunAttachments;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class GunBayonetDirectionalTests
{
    [TestCase(true, false, .8f, true)]
    [TestCase(true, true, 1.6f, false)]
    [TestCase(false, false, .8f, false)]
    [TestCase(true, false, 3f, false)]
    [TestCase(true, false, .8f, true, "N14WeaponSMG9mm", false)]
    [TestCase(true, false, .8f, true, "N14WeaponSMG9mm", true)]
    [TestCase(true, false, 1.6f, true, "N14WeaponSMG9mm", true)]
    public async Task StabUsesDirectionAndRespectsRangeAndObstructions(bool bayonet, bool wall, float distance, bool hits, string prototype = "N14WeaponRifle556R91", bool wielded = false)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var origin = map.GridCoords.Offset(new Vector2(.5f, .5f));
        EntityUid user = default, gun = default, front = default, side = default, behind = default;
        EntityUid? obstacle = null;
        EntityUid aim = default;
        await pair.Server.WaitPost(() =>
        {
            for (var x = -3; x <= 4; x++)
            for (var y = -3; y <= 3; y++)
                entities.System<SharedMapSystem>().SetTile(map.Grid, origin.Offset(new Vector2(x, y)), map.Tile.Tile);
            user = entities.SpawnEntity("MobHuman", origin);
            gun = entities.SpawnEntity(prototype, origin);
            if (bayonet)
            {
                var knife = entities.SpawnEntity("N14CombatKnife", origin);
                Assert.That(entities.System<ItemSlotsSystem>().TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, knife, null), Is.True);
            }
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            if (wielded)
                Assert.That(entities.System<WieldableSystem>().TryWield(gun, entities.GetComponent<WieldableComponent>(gun), user), Is.True);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            front = entities.SpawnEntity("MobHuman", origin.Offset(new Vector2(distance, .08f)));
            side = entities.SpawnEntity("MobHuman", origin.Offset(new Vector2(0, 1.2f)));
            behind = entities.SpawnEntity("MobHuman", origin.Offset(new Vector2(-1.2f, 0)));
            aim = entities.SpawnEntity(null, origin.Offset(new Vector2(20, 0)));
            if (wall) obstacle = entities.SpawnEntity("WallSolid", origin.Offset(new Vector2(.8f, 0)));
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            if (obstacle is { } blockingWall)
                Assert.That(entities.GetComponent<TransformComponent>(blockingWall).Anchored, Is.True);
            var melee = entities.GetComponent<MeleeWeaponComponent>(gun);
            melee.NextAttack = TimeSpan.Zero;
            // No target sprite: the cursor is far beyond melee reach.
            if (bayonet)
                Assert.That(entities.System<MeleeWeaponSystem>().AttemptWideAttack(user, gun, melee, aim), Is.True);
            else
                entities.System<MeleeWeaponSystem>().AttemptLightAttackMiss(user, gun, melee,
                    origin.Offset(new Vector2(20, 0)));
            Assert.That(entities.GetComponent<DamageableComponent>(front).TotalDamage.Float() > 0, Is.EqualTo(hits));
            Assert.That(entities.GetComponent<DamageableComponent>(side).TotalDamage.Float(), Is.Zero);
            Assert.That(entities.GetComponent<DamageableComponent>(behind).TotalDamage.Float(), Is.Zero);
            foreach (var entity in new EntityUid?[] { user, front, side, behind, aim, obstacle })
                if (entity is { } uid && entities.EntityExists(uid)) entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }
    [TestCase(0f)]
    [TestCase(1f)]
    public async Task WieldedSmgClientStabDamagesTarget(float cursorHeight)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid user = default, gun = default, target = default;
        NetEntity netUser = default, netGun = default, netTarget = default;
        var origin = map.GridCoords.Offset(new Vector2(.5f, .5f));
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = -3; x <= 4; x++)
            for (var y = -3; y <= 3; y++)
                server.System<SharedMapSystem>().SetTile(map.Grid, origin.Offset(new Vector2(x, y)), map.Tile.Tile);
            user = server.SpawnEntity("MobHuman", origin);
            gun = server.SpawnEntity("N14WeaponSMG9mm", origin);
            var knife = server.SpawnEntity("N14CombatKnife", origin);
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, knife, null), Is.True);
            Assert.That(server.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            Assert.That(server.System<WieldableSystem>().TryWield(gun, server.GetComponent<WieldableComponent>(gun), user), Is.True);
            server.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            target = server.SpawnEntity("MobHuman", origin.Offset(new Vector2(.8f, 0)));
            netTarget = server.GetNetEntity(target);
            netUser = server.GetNetEntity(user);
            netGun = server.GetNetEntity(gun);
            pair.Server.PlayerMan.SetAttachedEntity(session, user);
        });
        await pair.RunTicksSync(120);
        await pair.Client.WaitAssertion(() =>
        {
            var localGun = client.GetEntity(netGun);
            var localUser = client.GetEntity(netUser);
            var melee = client.GetComponent<MeleeWeaponComponent>(localGun);
            var system = client.System<Content.Client.Weapons.Melee.MeleeWeaponSystem>();
            var method = typeof(Content.Client.Weapons.Melee.MeleeWeaponSystem).GetMethod("ClientHeavyAttack",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var coordinates = client.GetComponent<TransformComponent>(localUser).Coordinates.Offset(cursorHeight == 0f ? new Vector2(20, 0) : new Vector2(.8f, cursorHeight));
            var aimMethod = typeof(Content.Client.Weapons.Melee.MeleeWeaponSystem).GetMethod("GetBayonetAimCoordinates",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            coordinates = (Robust.Shared.Map.EntityCoordinates) aimMethod.Invoke(system,
                new object?[] { coordinates, cursorHeight == 0f ? null : client.GetEntity(netTarget) })!;
            method.Invoke(system, new object[] { localUser, coordinates, localGun, melee });
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
            Assert.That(server.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.GreaterThan(0)));
        await pair.Server.WaitPost(() =>
        {
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
            server.DeleteEntity(user);
            server.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

}
