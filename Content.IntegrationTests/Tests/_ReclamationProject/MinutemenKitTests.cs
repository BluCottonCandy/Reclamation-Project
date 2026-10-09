using System.Collections.Generic;
using System.Linq;
using Content.Server.Power.Components;
using Content.Shared._ReclamationProject.Weapons.Ranged;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class MinutemenKitTests
{
    [TestCase(0, 75)]
    [TestCase(1, 85)]
    [TestCase(6, 145)]
    public async Task ScattergunUsesNormalCellAndSplitsCrankBonus(int charges, int totalDamage)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings());
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var gun = entities.SpawnEntity("MinutemenWeaponLaserShotgun", MapCoordinates.Nullspace);
            var slots = entities.System<ItemSlotsSystem>();
            var cell = slots.GetItemOrNull(gun, "gun_magazine");
            Assert.That(cell, Is.Not.Null);
            Assert.That(entities.GetComponent<MetaDataComponent>(cell!.Value).EntityPrototype!.ID,
                Is.EqualTo("N14MicrofusionCell"));
            var battery = entities.GetComponent<BatteryComponent>(cell.Value);
            var before = battery.CurrentCharge;
            var ammo = new TakeAmmoEvent(1, new(), entities.GetComponent<TransformComponent>(gun).Coordinates);
            entities.EventBus.RaiseLocalEvent(cell.Value, ammo, true);
            Assert.That(ammo.Ammo.Count, Is.EqualTo(1));
            Assert.That(ammo.Ammo[0].Entity, Is.Not.Null, "Must fire projectiles rather than the cell's beam.");
            Assert.That(before - battery.CurrentCharge, Is.EqualTo(75), "A standard cell should supply 16 uncranked blasts.");
            var pellets = new List<EntityUid> { ammo.Ammo[0].Entity!.Value };
            for (var i = 1; i < 5; i++)
                pellets.Add(entities.SpawnEntity("MinutemenLaserShotgunPellet", MapCoordinates.Nullspace));
            var crank = entities.GetComponent<CrankChargeComponent>(gun);
            crank.Charges = charges;
            entities.EventBus.RaiseLocalEvent(gun, new AmmoShotEvent { FiredProjectiles = pellets }, true);
            var damage = pellets.Sum(p => (float) entities.GetComponent<ProjectileComponent>(p).Damage.GetTotal());
            Assert.That(damage, Is.EqualTo(totalDamage).Within(0.01));
            var shot = new GunShotEvent(gun, ammo.Ammo);
            entities.EventBus.RaiseLocalEvent(gun, ref shot, true);
            Assert.That(crank.Charges, Is.Zero);
            foreach (var pellet in pellets)
                entities.DeleteEntity(pellet);
            entities.DeleteEntity(gun);
        });
        await pair.CleanReturnAsync();
    }
}
