using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Misfits.CCVar;
using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class GunAttachmentFiringTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: ReclamationGunLaserSight
  id: TestGunLaserSightPerfect
  components:
  - type: GunLaserSight
    accuracyMultiplier: 0
";

    [TestCase("N14WeaponRifle556R91", "ReclamationGunGrip", GunAttachmentsSystem.GripSlotId, "N14Cartridge556Rifle", false, false, 0.7)]
    [TestCase("N14WeaponRifle556R91", "ReclamationGunLaserSight", GunAttachmentsSystem.LaserSlotId, "N14Cartridge556Rifle", false, false, 0.7)]
    [TestCase("N14WeaponSMG10mm", "ReclamationGunStock", GunAttachmentsSystem.StockSlotId, "N14CartridgePistol10", true, false, 0.7)]
    [TestCase("N14WeaponRifle556R91", "ReclamationGunBipod", GunAttachmentsSystem.GripSlotId, "N14Cartridge556Rifle", false, true, 0.7)]
    [TestCase("N14WeaponShotgun", "ReclamationGunGrip", GunAttachmentsSystem.GripSlotId, "N14ShellShotgun12", false, false, 0.7)]
    [TestCase("N14WeaponShotgun", "ReclamationGunLaserSight", GunAttachmentsSystem.LaserSlotId, "N14ShellShotgun12", false, false, 0.7)]
    [TestCase("N14WeaponRifle556R91", "TestGunLaserSightPerfect", GunAttachmentsSystem.LaserSlotId, "N14Cartridge556Rifle", false, false, 0.0)]
    [TestCase("N14WeaponShotgun", "TestGunLaserSightPerfect", GunAttachmentsSystem.LaserSlotId, "N14ShellShotgun12", false, false, 0.0)]
    [TestCase("N14WeaponRifle556R91", "ReclamationGunHoloSight", GunAttachmentsSystem.HoloSlotId, "N14Cartridge556Rifle", false, false, 0.9)]
    public async Task AttachmentTightensActualProjectileTrajectories(string gunPrototype, string attachmentPrototype,
        string slot, string ammoPrototype, bool wielded, bool prone, double multiplier)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var cfg = pair.Server.ResolveDependency<IConfigurationManager>();
        var originalPrediction = cfg.GetCVar(PerformanceCVars.GunPrediction);
        EntityUid user = default, gun = default, attachment = default;
        Func<double[]> fire = default!;
        double[] baseline = default!;
        double baselineBloom = 0;
        await pair.RunTicksSync(120);
        await pair.Server.WaitAssertion(() =>
        {
            cfg.SetCVar(PerformanceCVars.GunPrediction, false);
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            gun = entities.SpawnEntity(gunPrototype, map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            if (wielded)
                Assert.That(entities.System<WieldableSystem>().TryWield(gun, entities.GetComponent<WieldableComponent>(gun), user), Is.True);
            if (prone)
                Assert.That(entities.System<StandingStateSystem>().Down(user, playSound: false, dropHeldItems: false), Is.True);
            var component = entities.GetComponent<GunComponent>(gun);
            var random = pair.Server.ResolveDependency<IRobustRandom>();
            double[] Fire()
            {
                var ammo = entities.SpawnEntity(ammoPrototype, map.GridCoords);
                var ammoComp = entities.GetComponent<CartridgeAmmoComponent>(ammo);
                random.SetSeed(73421);
                var projectiles = entities.System<GunSystem>().Shoot(gun, component,
                    new List<(EntityUid?, IShootable)> { (ammo, ammoComp) }, map.GridCoords,
                    map.GridCoords.Offset(new Vector2(20, 0)), out _, user)!;
                Assert.That(projectiles.Count, Is.EqualTo(ammoPrototype == "N14ShellShotgun12" ? 6 : 1));
                var angles = projectiles.Select(uid =>
                {
                    var velocity = entities.GetComponent<PhysicsComponent>(uid).LinearVelocity;
                    Assert.That(velocity.LengthSquared(), Is.GreaterThan(0f));
                    return Math.Atan2(velocity.Y, velocity.X);
                }).ToArray();
                foreach (var projectile in projectiles) entities.DeleteEntity(projectile);
                if (entities.EntityExists(ammo)) entities.DeleteEntity(ammo);
                return angles;
            }
            fire = Fire;
            baseline = fire();
            baselineBloom = component.CurrentAngle.Theta;
            Assert.That(baseline.Any(angle => Math.Abs(angle) > 0.0001), Is.True);
        });
        // Compare equally rested shots, not a cold shot against subsequent burst bloom.
        await pair.RunTicksSync(120);
        await pair.Server.WaitAssertion(() =>
        {
            var component = entities.GetComponent<GunComponent>(gun);
            attachment = entities.SpawnEntity(attachmentPrototype, map.GridCoords);
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(gun, slot, attachment, null), Is.True);
            if (prone) entities.EventBus.RaiseLocalEvent(gun, new ToggleGunBipodActionEvent { Performer = user });
            Assert.That(component.CurrentAngle.Theta, Is.EqualTo(baselineBloom * multiplier).Within(0.00001),
                "Changing attachments must update bloom that already accumulated from earlier shots.");
            var improved = fire();
            for (var i = 0; i < improved.Length; i++)
                Assert.That(improved[i], Is.EqualTo(baseline[i] * multiplier).Within(0.00001), "Attachment must change the fired projectile's direction, not just displayed gun stats.");
            Assert.That(slots.TryEject(gun, slot, null, out _), Is.True);
        });
        await pair.RunTicksSync(120);
        await pair.Server.WaitAssertion(() =>
        {
            var restored = fire();
            for (var i = 0; i < restored.Length; i++) Assert.That(restored[i], Is.EqualTo(baseline[i]).Within(0.00001));
            entities.DeleteEntity(attachment);
            entities.DeleteEntity(user);
            cfg.SetCVar(PerformanceCVars.GunPrediction, originalPrediction);
        });
        await pair.CleanReturnAsync();
    }
}
