using System.Numerics;
using Content.Server.Weapons.Melee;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class GuaranteedShoveTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ValidShovesConnectWithoutTrainingAndPreserveWeaponKnockoutChance(bool holdingItem)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0.5f, 0)));
            var combat = entities.GetComponent<CombatModeComponent>(user);
            entities.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
            combat.CanDisarm = true;
            combat.BaseDisarmFailChance = 1f;
            entities.GetComponent<StaminaComponent>(target).CritThreshold = 10000;
            var hands = entities.System<SharedHandsSystem>();
            EntityUid? item = null;
            if (holdingItem)
            {
                item = entities.SpawnEntity("Crowbar", map.GridCoords);
                Assert.That(hands.TryPickup(target, item.Value), Is.True);
            }
            var weapon = entities.GetComponent<MeleeWeaponComponent>(user);
            var melee = entities.System<MeleeWeaponSystem>();
            for (var i = 0; i < 16; i++)
            {
                weapon.NextAttack = TimeSpan.Zero;
                Assert.That(melee.AttemptDisarmAttack(user, user, weapon, target), Is.True, $"Shove {i}");
            }
            Assert.That(entities.GetComponent<PhysicsComponent>(target).LinearVelocity.LengthSquared(), Is.GreaterThan(0));
            if (item != null)
            {
                Assert.That(hands.TryGetActiveItem(target, out var held), Is.True);
                Assert.That(held, Is.EqualTo(item), "Guaranteed shoves must not guarantee a weapon knockout.");
                entities.DeleteEntity(item.Value);
            }
            var distant = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(10, 0)));
            weapon.NextAttack = TimeSpan.Zero;
            Assert.That(melee.AttemptDisarmAttack(user, user, weapon, distant), Is.False, "Shoves must still respect range.");
            entities.DeleteEntity(distant);
            entities.DeleteEntity(user);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }
}
