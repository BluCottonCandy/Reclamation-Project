using System;
using System.Linq;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Timing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class RevolverBoxLoadingTests
{
    [TestCase("N14WeaponRevolver9mm", "MagazineBox9mm")]
    [TestCase("N14WeaponRevolver10mm", "MagazineBox10mm")]
    [TestCase("N14WeaponRevolver44Magnum", "MagazineBox44")]
    [TestCase("N14WeaponRevolver45-70Hunter", "MagazineBox45-70")]
    public async Task BoxesFillOnlyEmptyChambersAndConserveAmmunition(string gunId, string boxId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid gun = default, box = default, user = default;
        RevolverAmmoProviderComponent cylinder = default!;
        BallisticAmmoProviderComponent source = default!;
        var count = 0;
        await pair.Server.WaitAssertion(() =>
        {
            gun = em.SpawnEntity(gunId, map.GridCoords);
            box = em.SpawnEntity(boxId, map.GridCoords);
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, box), Is.True);
            cylinder = em.GetComponent<RevolverAmmoProviderComponent>(gun);
            source = em.GetComponent<BallisticAmmoProviderComponent>(box);
            Array.Fill(cylinder.Chambers, (bool?) null);
            cylinder.Chambers[0] = true;
            count = source.AmmoCount;
            var system = em.System<Content.Server.Weapons.Ranged.Systems.GunSystem>();
            Assert.That(system.TryRevolverInsert(gun, cylinder, box, user), Is.True);
            Assert.That(system.TryRevolverInsert(gun, cylinder, box, user), Is.False, "Repeated clicks must not start parallel reloads.");
            Assert.That(source.AmmoCount, Is.EqualTo(count));
        });
        var tickRate = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        await pair.RunTicksSync((int) (tickRate * .9f));
        await pair.Server.WaitAssertion(() => Assert.That(source.AmmoCount, Is.EqualTo(count)));
        await pair.RunTicksSync((int) (tickRate * .3f));
        await pair.Server.WaitAssertion(() => Assert.That(source.AmmoCount, Is.EqualTo(count - 1), "Only one round is inserted after the first second."));
        await pair.RunTicksSync((int) tickRate * (cylinder.Capacity + 1));
        await pair.Server.WaitAssertion(() =>
        {
            var system = em.System<Content.Server.Weapons.Ranged.Systems.GunSystem>();
            Assert.That(cylinder.Chambers.Count(c => c == true), Is.EqualTo(cylinder.Capacity));
            Assert.That(source.AmmoCount, Is.EqualTo(count - cylinder.Capacity + 1));
            Assert.That(cylinder.AmmoSlots, Does.Not.Contain(box));
            Assert.That(system.TryRevolverInsert(gun, cylinder, box, user), Is.False);
            Assert.That(source.AmmoCount, Is.EqualTo(count - cylinder.Capacity + 1), "Full cylinder must not consume ammunition.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WrongCaliberAndEmptyBoxesDoNotLoadOrLoseRounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var gun = em.SpawnEntity("N14WeaponRevolver9mm", map.GridCoords);
            var box = em.SpawnEntity("MagazineBox10mm", map.GridCoords);
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, box), Is.True);
            var cylinder = em.GetComponent<RevolverAmmoProviderComponent>(gun);
            Array.Fill(cylinder.Chambers, (bool?) null);
            var source = em.GetComponent<BallisticAmmoProviderComponent>(box);
            var count = source.AmmoCount;
            var system = em.System<Content.Server.Weapons.Ranged.Systems.GunSystem>();
            Assert.That(system.TryRevolverInsert(gun, cylinder, box, user), Is.False);
            Assert.That(source.AmmoCount, Is.EqualTo(count));
            Assert.That(cylinder.Chambers, Is.All.Null);
            var empty = em.SpawnEntity("MagazineBox9mm", map.GridCoords);
            em.GetComponent<BallisticAmmoProviderComponent>(empty).UnspawnedCount = 0;
            Assert.That(system.TryRevolverInsert(gun, cylinder, empty, user), Is.False);
            Assert.That(cylinder.Chambers, Is.All.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MixedBoxKeepsWrongRoundsAndPreservesSpentCartridgeState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid gun = default, box = default, wrong = default, spent = default;
        RevolverAmmoProviderComponent cylinder = default!;
        BallisticAmmoProviderComponent source = default!;
        await pair.Server.WaitAssertion(() =>
        {
            gun = em.SpawnEntity("N14WeaponRevolver9mm", map.GridCoords);
            box = em.SpawnEntity("MagazineBox9mm", map.GridCoords);
            cylinder = em.GetComponent<RevolverAmmoProviderComponent>(gun);
            Array.Fill(cylinder.Chambers, (bool?) null);
            source = em.GetComponent<BallisticAmmoProviderComponent>(box);
            source.UnspawnedCount = 0;
            wrong = em.SpawnEntity("N14CartridgePistol10", map.GridCoords);
            spent = em.SpawnEntity("N14CartridgePistol9", map.GridCoords);
            em.GetComponent<CartridgeAmmoComponent>(spent).Spent = true;
            var containers = em.System<SharedContainerSystem>();
            Assert.That(containers.Insert(wrong, source.Container), Is.True);
            Assert.That(containers.Insert(spent, source.Container), Is.True);
            source.SpawnedCountPredict = 2;
            var system = em.System<Content.Server.Weapons.Ranged.Systems.GunSystem>();
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, box), Is.True);
            Assert.That(system.TryRevolverInsert(gun, cylinder, box, user), Is.True);
        });
        await pair.RunTicksSync((int) pair.Server.ResolveDependency<IGameTiming>().TickRate * 3);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(source.AmmoCount, Is.EqualTo(1));
            Assert.That(source.Container.ContainedEntities, Does.Contain(wrong));
            Assert.That(cylinder.AmmoSlots, Does.Contain(spent));
            Assert.That(cylinder.Chambers.Count(c => c == false), Is.EqualTo(1));
            Assert.That(cylinder.Chambers.Count(c => c == true), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }
    [TestCase("N14MagazinePistol9mm")]
    [TestCase("N14MagazineSMG9mm")]
    public async Task BoxesLoadMagazinesOneRoundPerHalfSecond(string magazineId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid box = default, magazine = default;
        BallisticAmmoProviderComponent source = default!, receiver = default!;
        await pair.Server.WaitAssertion(() =>
        {
            box = em.SpawnEntity("MagazineBox9mm", map.GridCoords);
            magazine = em.SpawnEntity(magazineId, map.GridCoords);
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, box), Is.True);
            source = em.GetComponent<BallisticAmmoProviderComponent>(box);
            receiver = em.GetComponent<BallisticAmmoProviderComponent>(magazine);
            source.UnspawnedCount = 2;
            receiver.UnspawnedCount = 0;
            em.EventBus.RaiseLocalEvent(box, new Content.Shared.Interaction.AfterInteractEvent(user, box, magazine, map.GridCoords, true));
        });
        var ticks = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        await pair.RunTicksSync((int) (ticks * .4f));
        await pair.Server.WaitAssertion(() => Assert.That(receiver.AmmoCount, Is.Zero));
        await pair.RunTicksSync((int) (ticks * .2f));
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(receiver.AmmoCount, Is.EqualTo(1));
            Assert.That(source.AmmoCount, Is.EqualTo(1));
        });
        await pair.RunTicksSync((int) ticks * 3);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(receiver.AmmoCount, Is.EqualTo(2));
            Assert.That(source.AmmoCount, Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

}
