using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
public sealed class SynthIntegratedGunTest
{
    [Test]
    public async Task RiflemanUsesFiniteRaiderAmmoAndReloads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid synth = default;
        await pair.Server.WaitAssertion(() =>
        {
            synth = entities.SpawnEntity("ReclamationMobGenOneSynthLaser", map.GridCoords);
            Assert.That(entities.HasComponent<HitscanBatteryAmmoProviderComponent>(synth), Is.False);
            var ammo = entities.GetComponent<BasicEntityAmmoProviderComponent>(synth);
            Assert.That(ammo.Count, Is.EqualTo(10));
            Assert.That(ammo.Capacity, Is.EqualTo(10));
            Assert.That(entities.GetComponent<RechargeBasicEntityAmmoComponent>(synth).RechargeCooldown, Is.EqualTo(2));
        });
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(() =>
        {
            var ammo = entities.GetComponent<BasicEntityAmmoProviderComponent>(synth);
            var take = new TakeAmmoEvent(11, new(), map.GridCoords);
            entities.EventBus.RaiseLocalEvent(synth, take);
            Assert.That(take.Ammo.Count, Is.EqualTo(10), "The integrated gun must run out after ten shots");
            Assert.That(ammo.Count, Is.Zero);
            foreach (var shot in take.Ammo)
            {
                Assert.That(shot.Entity, Is.Not.Null);
                Assert.That(entities.HasComponent<ProjectileComponent>(shot.Entity!.Value), Is.True);
                entities.DeleteEntity(shot.Entity);
            }
        });
        await pair.RunSeconds(2.2f);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<BasicEntityAmmoProviderComponent>(synth).Count, Is.InRange(1, 2),
                "Raider reload behavior should gradually replenish ammo without instantly refilling");
            entities.DeleteEntity(synth);
        });
        await pair.CleanReturnAsync();
    }
}
