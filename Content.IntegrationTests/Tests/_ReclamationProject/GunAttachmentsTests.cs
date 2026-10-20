using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Actions;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class GunAttachmentsTests
{
    [TestCase("N14WeaponPistol9mm")]
    [TestCase("N14WeaponPistol10mm")]
    public async Task SuppressorChangesSoundAndFlashWithoutChangingDamageOrStacking(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var gun = entities.SpawnEntity(prototype, map.GridCoords);
            var item = entities.SpawnEntity("ReclamationGunSuppressor", map.GridCoords);
            var gunComp = entities.GetComponent<GunComponent>(gun);
            var volume = gunComp.SoundGunshotModified!.Params.Volume;
            var baseVolume = gunComp.SoundGunshot!.Params.Volume;
            var hadMelee = entities.TryGetComponent<MeleeWeaponComponent>(gun, out var originalMelee);
            var damage = originalMelee?.Damage;
            var rate = gunComp.FireRateModified;
            var speed = gunComp.ProjectileSpeedModified;
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, item, null), Is.True);
            Assert.That(gunComp.SoundGunshotModified!.Params.Volume, Is.EqualTo(volume - 12f));
            Assert.That(gunComp.SoundGunshot.Params.Volume, Is.EqualTo(baseVolume));
            for (var i = 0; i < 3; i++) entities.System<SharedGunSystem>().RefreshModifiers(gun);
            Assert.That(gunComp.SoundGunshotModified!.Params.Volume, Is.EqualTo(volume - 12f));
            Assert.That(gunComp.FireRateModified, Is.EqualTo(rate));
            Assert.That(gunComp.ProjectileSpeedModified, Is.EqualTo(speed * 0.9f).Within(0.0001f));
            Assert.That(entities.TryGetComponent<MeleeWeaponComponent>(gun, out var currentMelee), Is.EqualTo(hadMelee));
            if (hadMelee) Assert.That(currentMelee!.Damage.DamageDict, Is.EquivalentTo(damage!.DamageDict));
            var flash = new GunMuzzleFlashAttemptEvent(false);
            entities.EventBus.RaiseLocalEvent(gun, ref flash);
            Assert.That(flash.Cancelled, Is.True);
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.MuzzleSlotId, null, out _), Is.True);
            Assert.That(gunComp.SoundGunshotModified!.Params.Volume, Is.EqualTo(volume));
            Assert.That(gunComp.ProjectileSpeedModified, Is.EqualTo(speed));
            flash = new GunMuzzleFlashAttemptEvent(false);
            entities.EventBus.RaiseLocalEvent(gun, ref flash);
            Assert.That(flash.Cancelled, Is.False);
            entities.DeleteEntity(gun);
            entities.DeleteEntity(item);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("N14WeaponLaserRifle")]
    [TestCase("N14WeaponLaserPistol")]
    [TestCase("N14WeaponRifle556R91")]
    [TestCase("N14WeaponSMG10mm")]
    [TestCase("N14WeaponShotgun")]
    [TestCase("N14WeaponRevolver9mm")]
    [TestCase("N14WeaponRifle556CarbineOldMakeshift")]
    public async Task EnergyWeaponsRejectSuppressors(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var gun = entities.SpawnEntity(prototype, map.GridCoords);
            var suppressor = entities.SpawnEntity("ReclamationGunSuppressor", map.GridCoords);
            Assert.That(entities.System<ItemSlotsSystem>().TryInsert(gun,
                GunAttachmentsSystem.MuzzleSlotId, suppressor, null), Is.False);
            entities.DeleteEntity(gun);
            entities.DeleteEntity(suppressor);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GripImprovesStabilityByThirtyPercentAndRestoresOnRemoval()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity("N14WeaponRifle556R91", map.GridCoords);
            var grip = entities.SpawnEntity("ReclamationGunGrip", map.GridCoords);
            var hands = entities.System<SharedHandsSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var gunComp = entities.GetComponent<GunComponent>(gun);
            var wield = entities.GetComponent<WieldableComponent>(gun);
            var wielding = entities.System<WieldableSystem>();
            Assert.That(hands.TryPickup(user, gun), Is.True);
            var unwieldedRecoil = gunComp.CameraRecoilScalarModified;
            Assert.That(wielding.TryWield(gun, wield, user), Is.True);
            var wieldedRecoil = gunComp.CameraRecoilScalarModified;
            var wieldedKick = gunComp.AngleIncreaseModified.Theta;
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.GripSlotId, grip, null), Is.True);
            Assert.That(gunComp.CameraRecoilScalarModified, Is.EqualTo(wieldedRecoil * 0.7f).Within(0.0001f));
            Assert.That(gunComp.AngleIncreaseModified.Theta, Is.EqualTo(wieldedKick * 0.7f).Within(0.0001));
            Assert.That(wielding.TryUnwield(gun, wield, user), Is.True);
            Assert.That(gunComp.CameraRecoilScalarModified, Is.EqualTo(unwieldedRecoil * 0.7f).Within(0.0001f));
            Assert.That(wielding.TryWield(gun, wield, user), Is.True);
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.GripSlotId, null, out _), Is.True);
            Assert.That(gunComp.CameraRecoilScalarModified, Is.EqualTo(wieldedRecoil));
            Assert.That(gunComp.AngleIncreaseModified.Theta, Is.EqualTo(wieldedKick));
            entities.DeleteEntity(user);
            entities.DeleteEntity(grip);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RailLightGrantsHeldActionAndShinesThroughMount(bool attachWhileHeld)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity("N14WeaponPistol10mm", map.GridCoords);
            var light = entities.SpawnEntity("ReclamationGunLight", map.GridCoords);
            var hands = entities.System<SharedHandsSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            if (attachWhileHeld) Assert.That(hands.TryPickup(user, gun), Is.True);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.LightSlotId, light, null), Is.True);
            if (!attachWhileHeld) Assert.That(hands.TryPickup(user, gun), Is.True);
            var holder = entities.GetComponent<GunAttachmentsComponent>(gun);
            var flashlight = entities.GetComponent<UnpoweredFlashlightComponent>(light);
            var actions = entities.System<SharedActionsSystem>();
            Assert.That(holder.LightSlot.ContainerSlot!.OccludesLight, Is.False);
            Assert.That(holder.LightAction, Is.Not.Null);
            Assert.That(actions.TryGetActionData(holder.LightAction!.Value, out var action), Is.True);
            Assert.That(action!.AttachedEntity, Is.EqualTo(user));
            var toggle = new ToggleWeaponLightActionEvent { Performer = user };
            entities.EventBus.RaiseLocalEvent(gun, toggle);
            Assert.That(toggle.Handled, Is.True);
            Assert.That(flashlight.LightOn, Is.True);
            Assert.That(action.Toggled, Is.True);
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.LightSlotId, null, out _), Is.True);
            Assert.That(action.AttachedEntity, Is.Null);
            Assert.That(action.Toggled, Is.False);
            entities.DeleteEntity(user);
            entities.DeleteEntity(light);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MuzzleIsExclusiveWhileGripAndLightCanCoexist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var gun = entities.SpawnEntity("N14WeaponRifle556R91", map.GridCoords);
            var knife = entities.SpawnEntity("N14CombatKnife", map.GridCoords);
            var suppressor = entities.SpawnEntity("ReclamationGunSuppressor", map.GridCoords);
            var grip = entities.SpawnEntity("ReclamationGunGrip", map.GridCoords);
            var light = entities.SpawnEntity("ReclamationGunLight", map.GridCoords);
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(gun, "gun_scope", out _), Is.False);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, knife, null), Is.True);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, suppressor, null), Is.False);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.GripSlotId, grip, null), Is.True);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.LightSlotId, light, null), Is.True);
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.MuzzleSlotId, null, out _), Is.True);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, suppressor, null), Is.False);
            Assert.That(entities.GetComponent<GunAttachmentsComponent>(gun).Applied, Is.False);
            entities.DeleteEntity(gun);
            entities.DeleteEntity(knife);
        });
        await pair.CleanReturnAsync();
    }
}
