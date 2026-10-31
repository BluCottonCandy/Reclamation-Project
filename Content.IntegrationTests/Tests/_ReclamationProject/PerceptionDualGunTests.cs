using System;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Misfits.CCVar;
using Content.Shared._Misfits.Special;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class PerceptionDualGunTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseItem
  id: TestPerceptionDualGun
  components:
  - type: Gun
    fireRate: 4
    selectedMode: FullAuto
    availableModes:
    - FullAuto
  - type: BasicEntityAmmoProvider
    proto: BulletPistol
    capacity: 8
";

    [Test]
    public async Task OffhandRequestsRequireTenPerceptionAndTwoOneHandedHeldGuns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var config = pair.Server.ResolveDependency<IConfigurationManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        await pair.Server.WaitAssertion(() =>
        {
            var prediction = config.GetCVar(PerformanceCVars.GunPrediction);
            config.SetCVar(PerformanceCVars.GunPrediction, false);
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<Content.Shared._Misfits.Special.Components.SpecialComponent>(user);
            var primary = em.SpawnEntity("TestPerceptionDualGun", map.GridCoords);
            var secondary = em.SpawnEntity("TestPerceptionDualGun", map.GridCoords);
            var remote = em.SpawnEntity("TestPerceptionDualGun", map.GridCoords);
            try
            {
                pair.Server.PlayerMan.SetAttachedEntity(session, user);
                em.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
                var hands = em.System<SharedHandsSystem>();
                Assert.That(hands.TryPickup(user, primary), Is.True);
                var handComp = em.GetComponent<HandsComponent>(user);
                var otherHand = handComp.Hands.Values.Single(h => h.HeldEntity == null);
                Assert.That(hands.TryPickup(user, secondary, otherHand.Name), Is.True);
                var special = em.System<SharedSpecialSystem>();
                var guns = em.System<GunSystem>();
                var primaryGun = em.GetComponent<GunComponent>(primary);
                var secondaryGun = em.GetComponent<GunComponent>(secondary);
                var primaryAmmo = em.GetComponent<BasicEntityAmmoProviderComponent>(primary);
                var secondaryAmmo = em.GetComponent<BasicEntityAmmoProviderComponent>(secondary);
                var target = em.GetNetCoordinates(map.GridCoords.Offset(new System.Numerics.Vector2(10, 0)));
                void Fire(EntityUid gun) => guns.ShootRequested(em.GetNetEntity(gun), target, null, null, session);
                // Test setup deliberately rewinds the cooldown between independent authorization cases.
#pragma warning disable RA0002
                void Ready() { primaryGun.NextFire = secondaryGun.NextFire = timing.CurTime; }
#pragma warning restore RA0002
                special.TrySetBase(user, SpecialStat.Perception, 9);
                Ready(); Fire(secondary);
                Assert.That(secondaryAmmo.Count, Is.EqualTo(8), "A direct offhand request below Perception 10 is rejected.");
                special.TrySetBase(user, SpecialStat.Perception, 10);
                Ready(); Fire(primary); Fire(secondary);
                Assert.That(primaryAmmo.Count, Is.EqualTo(7));
                Assert.That(secondaryAmmo.Count, Is.EqualTo(7));
                Fire(primary); Fire(secondary);
                Assert.That(primaryAmmo.Count, Is.EqualTo(7), "Cooldowns cannot be bypassed.");
                Assert.That(secondaryAmmo.Count, Is.EqualTo(7));
                Fire(remote);
                Assert.That(em.GetComponent<BasicEntityAmmoProviderComponent>(remote).Count, Is.EqualTo(8));
                em.AddComponent<GunRequiresWieldComponent>(primary);
                Ready(); Fire(secondary);
                Assert.That(secondaryAmmo.Count, Is.EqualTo(7), "A two-handed primary blocks dual firing.");
                em.RemoveComponent<GunRequiresWieldComponent>(primary);
                em.AddComponent<GunRequiresWieldComponent>(secondary);
                Assert.That(guns.TryGetOffhandGun(user, primary, out _, out _), Is.False);
                em.RemoveComponent<GunRequiresWieldComponent>(secondary);
                special.TrySetBase(user, SpecialStat.Perception, 9);
                Ready(); Fire(secondary);
                Assert.That(secondaryAmmo.Count, Is.EqualTo(7), "Losing Perception removes authorization immediately.");
            }
            finally
            {
                pair.Server.PlayerMan.SetAttachedEntity(session, null);
                em.DeleteEntity(user);
                if (em.EntityExists(primary)) em.DeleteEntity(primary);
                if (em.EntityExists(secondary)) em.DeleteEntity(secondary);
                em.DeleteEntity(remote);
                config.SetCVar(PerformanceCVars.GunPrediction, prediction);
            }
        });
        await pair.CleanReturnAsync();
    }
}
