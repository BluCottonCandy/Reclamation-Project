using System.Collections.Generic;
using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class GunBayonetTests
{
    [Test]
    public async Task AttachedKnifeAndMeleeRangeReplicateToClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        EntityUid gun = default;
        EntityUid knife = default;
        NetEntity netGun = default;
        NetEntity netKnife = default;
        float originalRange = 0;
        await pair.Server.WaitAssertion(() =>
        {
            gun = server.SpawnEntity("N14WeaponRifle556R91", map.GridCoords);
            knife = server.SpawnEntity("N14CombatKnife", map.GridCoords);
            originalRange = server.GetComponent<MeleeWeaponComponent>(gun).Range;
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, knife, null), Is.True);
            netGun = server.GetNetEntity(gun);
            netKnife = server.GetNetEntity(knife);
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var localGun = client.GetEntity(netGun);
            Assert.That(client.HasComponent<GunAttachmentsComponent>(localGun), Is.True);
            Assert.That(client.System<ItemSlotsSystem>().TryGetSlot(localGun, GunAttachmentsSystem.MuzzleSlotId, out var slot), Is.True);
            Assert.That(slot!.Item, Is.EqualTo(client.GetEntity(netKnife)));
            Assert.That(client.GetComponent<MeleeWeaponComponent>(localGun).Range, Is.EqualTo(1.75f));
        });
        await pair.Server.WaitAssertion(() =>
            Assert.That(server.System<ItemSlotsSystem>().TryEject(gun, GunAttachmentsSystem.MuzzleSlotId, null, out _), Is.True));
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var localGun = client.GetEntity(netGun);
            Assert.That(client.GetComponent<MeleeWeaponComponent>(localGun).Range, Is.EqualTo(originalRange));
        });
        await pair.Server.WaitPost(() => { server.DeleteEntity(gun); server.DeleteEntity(knife); });
        await pair.CleanReturnAsync();
    }

    [TestCase("N14WeaponRifle556R91")]
    [TestCase("N14WeaponShotgun")]
    [TestCase("N14WeaponLaserRifle")]
    [TestCase("N14WeaponLaserRifleAuto")]
    [TestCase("N14WeaponLaserRifleMakeshift")]
    [TestCase("WeaponSniperMosin")]
    public async Task KnifeAttachesUsesMosinStabAndRestoresOriginalMelee(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity(prototype, map.GridCoords);
            var knife = entities.SpawnEntity("N14CombatKnife", map.GridCoords);
            var mount = entities.GetComponent<GunAttachmentsComponent>(gun);
            var slots = entities.System<ItemSlotsSystem>();
            var hadMelee = entities.TryGetComponent<MeleeWeaponComponent>(gun, out var original);
            var oldRange = original?.Range;
            var oldDamage = original?.Damage;
            var oldAnimation = original?.Animation;
            var oldRate = original?.AttackRate;
            var oldAngle = original?.Angle;
            var oldHeavyRange = original?.HeavyRangeModifier;
            var oldMaxTargets = original?.MaxTargets;
            var existingSlots = new Dictionary<string, EntityUid?>();
            foreach (var (key, slot) in entities.GetComponent<ItemSlotsComponent>(gun).Slots)
            {
                if (key != GunAttachmentsSystem.MuzzleSlotId)
                    existingSlots[key] = slot.Item;
            }
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, knife, null), Is.True);
            Assert.That(mount.MuzzleSlot.Item, Is.EqualTo(knife));
            var attached = entities.GetComponent<MeleeWeaponComponent>(gun);
            var template = entities.System<GunAttachmentsSystem>().GetTemplate();
            Assert.That(attached.Range, Is.EqualTo(template.Range));
            Assert.That(attached.Angle, Is.EqualTo(entities.System<GunAttachmentsSystem>().GetStabTemplate().Angle));
            Assert.That(attached.WideAnimation.Id, Is.EqualTo("WeaponArcThrust"));
            Assert.That(attached.HeavyRangeModifier, Is.EqualTo(1f));
            Assert.That(attached.MaxTargets, Is.EqualTo(1));
            Assert.That(attached.Animation, Is.EqualTo(template.Animation));
            Assert.That(attached.WideAnimationRotation, Is.EqualTo(template.WideAnimationRotation));
            foreach (var (key, item) in existingSlots)
            {
                Assert.That(slots.TryGetSlot(gun, key, out var slot), Is.True);
                Assert.That(slot!.Item, Is.EqualTo(item), $"Ammunition slot {key} changed.");
            }
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var reference = entities.SpawnEntity("WeaponSniperMosin", map.GridCoords);
            var expected = melee.GetDamage(reference, user);
            Assert.That(melee.GetDamage(gun, user).DamageDict, Is.EquivalentTo(expected.DamageDict));
            var hands = entities.System<SharedHandsSystem>();
            Assert.That(hands.TryPickup(user, gun), Is.True);
            Assert.That(entities.System<WieldableSystem>().TryWield(gun,
                entities.GetComponent<WieldableComponent>(gun), user), Is.True);
            var bonus = entities.GetComponent<IncreaseDamageOnWieldComponent>(reference).BonusDamage;
            Assert.That(melee.GetDamage(gun, user).DamageDict, Is.EquivalentTo((expected + bonus).DamageDict));
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.MuzzleSlotId, null, out var ejected), Is.True);
            Assert.That(ejected, Is.EqualTo(knife));
            Assert.That(entities.HasComponent<MeleeWeaponComponent>(gun), Is.EqualTo(hadMelee));
            if (hadMelee)
            {
                var restored = entities.GetComponent<MeleeWeaponComponent>(gun);
                Assert.That(restored.Range, Is.EqualTo(oldRange));
                Assert.That(restored.AttackRate, Is.EqualTo(oldRate));
                Assert.That(restored.Angle, Is.EqualTo(oldAngle));
                Assert.That(restored.HeavyRangeModifier, Is.EqualTo(oldHeavyRange));
                Assert.That(restored.MaxTargets, Is.EqualTo(oldMaxTargets));
                Assert.That(restored.Animation, Is.EqualTo(oldAnimation));
                Assert.That(restored.Damage.DamageDict, Is.EquivalentTo(oldDamage!.DamageDict));
            }
            entities.DeleteEntity(user);
            entities.DeleteEntity(knife);
            entities.DeleteEntity(reference);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RejectsOtherItemsAndDuplicateKnivesAndDoesNotModifyPistols()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var pistol = entities.SpawnEntity("N14WeaponPistol10mm", map.GridCoords);
            Assert.That(entities.HasComponent<GunAttachmentsComponent>(pistol), Is.True);
            Assert.That(entities.System<ItemSlotsSystem>().TryGetSlot(pistol, GunAttachmentsSystem.GripSlotId, out _), Is.False);
            var gun = entities.SpawnEntity("N14WeaponRifle556R91", map.GridCoords);
            var first = entities.SpawnEntity("N14CombatKnife", map.GridCoords);
            var second = entities.SpawnEntity("CombatKnife", map.GridCoords);
            var tool = entities.SpawnEntity("Crowbar", map.GridCoords);
            Assert.That(entities.System<ItemSlotsSystem>().TryInsert(pistol, GunAttachmentsSystem.MuzzleSlotId, first, null), Is.False);
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, tool, null), Is.False);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, first, null), Is.True);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, second, null), Is.False);
            Assert.That(slots.TryEject(gun, GunAttachmentsSystem.MuzzleSlotId, null, out _), Is.True);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.MuzzleSlotId, second, null), Is.True);
            entities.DeleteEntity(gun);
            entities.DeleteEntity(pistol);
            entities.DeleteEntity(first);
            entities.DeleteEntity(tool);
        });
        await pair.CleanReturnAsync();
    }
}
