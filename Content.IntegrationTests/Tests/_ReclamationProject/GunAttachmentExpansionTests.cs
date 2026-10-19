using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class GunAttachmentExpansionTests
{
    [TestCase("N14WeaponSMG10mm", true, false)]
    [TestCase("N14WeaponRifle556R91", false, true)]
    [TestCase("N14WeaponLaserRifle", false, true)]
    [TestCase("N14WeaponSniperHunting", false, true)]
    [TestCase("N14WeaponPistol10mm", false, false)]
    [TestCase("N14WeaponShotgun", false, false)]
    public async Task AttachmentCompatibilityAndLaserAccuracy(string prototype, bool stockAllowed, bool bipodAllowed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var gun = entities.SpawnEntity(prototype, map.GridCoords);
            var stock = entities.SpawnEntity("ReclamationGunStock", map.GridCoords);
            var bipod = entities.SpawnEntity("ReclamationGunBipod", map.GridCoords);
            var laser = entities.SpawnEntity("ReclamationGunLaserSight", map.GridCoords);
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.StockSlotId, stock, null), Is.EqualTo(stockAllowed));
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.GripSlotId, bipod, null), Is.EqualTo(bipodAllowed));
            var component = entities.GetComponent<GunComponent>(gun);
            var minimum = component.MinAngleModified.Theta;
            var maximum = component.MaxAngleModified.Theta;
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.LaserSlotId, laser, null), Is.True);
            Assert.That(component.MinAngleModified.Theta, Is.EqualTo(minimum * 0.7f).Within(0.00001));
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(maximum * 0.7f).Within(0.00001));
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.LaserSlotId, null, out _), Is.True);
            Assert.That(component.MinAngleModified.Theta, Is.EqualTo(minimum));
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(maximum));
            entities.DeleteEntity(gun);
            if (entities.EntityExists(stock)) entities.DeleteEntity(stock);
            if (entities.EntityExists(bipod)) entities.DeleteEntity(bipod);
            entities.DeleteEntity(laser);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("N14WeaponSMG10mm", true)]
    [TestCase("N14WeaponPistol10mm", true)]
    [TestCase("N14WeaponRifle556R91", true)]
    [TestCase("N14WeaponLaserRifle", true)]
    [TestCase("N14WeaponShotgun", true)]
    [TestCase("N14WeaponSniperHunting", false)]
    public async Task HoloCompatibilityAndAccuracy(string prototype, bool allowed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var gun = entities.SpawnEntity(prototype, map.GridCoords);
            var holo = entities.SpawnEntity("ReclamationGunHoloSight", map.GridCoords);
            var component = entities.GetComponent<GunComponent>(gun);
            var spread = component.MaxAngleModified.Theta;
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.HoloSlotId, holo, null), Is.EqualTo(allowed));
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(spread * (allowed ? .9f : 1f)).Within(.00001));
            if (allowed) Assert.That(slots.TryEject(gun, GunAttachmentsSystem.HoloSlotId, null, out _), Is.True);
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(spread));
            entities.DeleteEntity(gun);
            entities.DeleteEntity(holo);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MultipleAttachmentsStackAndRemovalPreservesRemainingBonuses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity("N14WeaponSMG10mm", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            var wield = entities.GetComponent<WieldableComponent>(gun);
            Assert.That(entities.System<WieldableSystem>().TryWield(gun, wield, user), Is.True);
            var component = entities.GetComponent<GunComponent>(gun);
            var spread = component.MaxAngleModified.Theta;
            var recoil = component.CameraRecoilScalarModified;
            var speed = component.ProjectileSpeedModified;
            var slots = entities.System<ItemSlotsSystem>();
            foreach (var (slot, prototype) in new[]
            {
                (GunAttachmentsSystem.GripSlotId, "ReclamationGunGrip"),
                (GunAttachmentsSystem.LaserSlotId, "ReclamationGunLaserSight"),
                (GunAttachmentsSystem.StockSlotId, "ReclamationGunStock"),
                (GunAttachmentsSystem.HoloSlotId, "ReclamationGunHoloSight"),
                (GunAttachmentsSystem.MuzzleSlotId, "ReclamationGunSuppressor"),
            })
                Assert.That(slots.TryInsert(gun, slot, entities.SpawnEntity(prototype, map.GridCoords), null), Is.True);
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(spread * .7f * .7f * .7f * .9f).Within(.00001));
            Assert.That(component.CameraRecoilScalarModified, Is.EqualTo(recoil * .7f * .7f).Within(.0001));
            Assert.That(component.ProjectileSpeedModified, Is.EqualTo(speed * .9f).Within(.0001));
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.LaserSlotId, null, out var laser), Is.True);
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(spread * .7f * .7f * .9f).Within(.00001));
            entities.DeleteEntity(laser);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StockBonusRequiresWielding()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity("N14WeaponSMG10mm", map.GridCoords);
            var stock = entities.SpawnEntity("ReclamationGunStock", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            var component = entities.GetComponent<GunComponent>(gun);
            var wield = entities.GetComponent<WieldableComponent>(gun);
            var wielding = entities.System<WieldableSystem>();
            var unwielded = component.CameraRecoilScalarModified;
            Assert.That(wielding.TryWield(gun, wield, user), Is.True);
            var recoil = component.CameraRecoilScalarModified;
            var maximum = component.MaxAngleModified.Theta;
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.StockSlotId, stock, null), Is.True);
            Assert.That(component.CameraRecoilScalarModified, Is.EqualTo(recoil * 0.7f).Within(0.0001f));
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(maximum * 0.7f).Within(0.00001));
            Assert.That(wielding.TryUnwield(gun, wield, user), Is.True);
            Assert.That(component.CameraRecoilScalarModified, Is.EqualTo(unwielded));
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BipodRequiresProneAndFoldsOnStandingDroppingOrRemoval()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity("N14WeaponRifle556R91", map.GridCoords);
            var bipod = entities.SpawnEntity("ReclamationGunBipod", map.GridCoords);
            var hands = entities.System<SharedHandsSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(hands.TryPickup(user, gun), Is.True);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.GripSlotId, bipod, null), Is.True);
            var holder = entities.GetComponent<GunAttachmentsComponent>(gun);
            var component = entities.GetComponent<GunComponent>(gun);
            var maximum = component.MaxAngleModified.Theta;
            void Toggle() => entities.EventBus.RaiseLocalEvent(gun, new ToggleGunBipodActionEvent { Performer = user });
            Toggle();
            Assert.That(holder.BipodDeployed, Is.False);
            Assert.That(entities.System<SharedLayingDownSystem>().TryLieDown(user), Is.True);
            // Lying down currently drops held items; recover the rifle before deploying.
            if (!hands.IsHolding(user, gun, out _)) Assert.That(hands.TryPickup(user, gun), Is.True);
            Toggle();
            Assert.That(holder.BipodDeployed, Is.True);
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(maximum * 0.7f).Within(0.00001));
            var speed = new RefreshMovementSpeedModifiersEvent();
            entities.EventBus.RaiseLocalEvent(user, speed);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(entities.System<StandingStateSystem>().Stand(user, force: true), Is.True);
            Assert.That(holder.BipodDeployed, Is.False);
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(maximum));
            Assert.That(entities.System<SharedLayingDownSystem>().TryLieDown(user), Is.True);
            // Lying down currently drops held items; recover the rifle before deploying.
            if (!hands.IsHolding(user, gun, out _)) Assert.That(hands.TryPickup(user, gun), Is.True);
            Toggle();
            Assert.That(hands.TryDrop(user, gun), Is.True);
            Assert.That(holder.BipodDeployed, Is.False);
            Assert.That(hands.TryPickup(user, gun), Is.True);
            Toggle();
            Assert.That(holder.BipodDeployed, Is.True);
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.GripSlotId, null, out _), Is.True);
            Assert.That(holder.BipodDeployed, Is.False);
            Assert.That(component.MaxAngleModified.Theta, Is.EqualTo(maximum));
            entities.DeleteEntity(user);
            entities.DeleteEntity(bipod);
        });
        await pair.CleanReturnAsync();
    }
}
