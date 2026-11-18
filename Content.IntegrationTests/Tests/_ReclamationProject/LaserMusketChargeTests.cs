using Content.Server.Power.Components;
using Content.Shared._ReclamationProject.Weapons.Ranged;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class LaserMusketChargeTests
{
    [TestCase("MinutemenWeaponLaserMusket", 6, 5)]
    [TestCase("MinutemenWeaponLaserShotgun", 6, 5)]
    [TestCase("MinutemenWeaponHeavyLaserMusket", 1, 3)]
    public async Task PaidChargesExpireOneAtATime(string prototype, int charges, int seconds)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        EntityUid gun = default, user = default;
        CrankChargeComponent crank = default!;
        BatteryComponent battery = default!;
        Action wind = default!;
        float startingEnergy = 0, remainingEnergy = 0;
        TimeSpan firstLoss = default;

        await pair.Server.WaitAssertion(() =>
        {
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            gun = entities.SpawnEntity(prototype, map.GridCoords);
            var hands = entities.System<SharedHandsSystem>();
            Assert.That(hands.TryPickup(user, gun), Is.True);
            var cell = entities.System<ItemSlotsSystem>().GetItemOrNull(gun, "gun_magazine")!.Value;
            battery = entities.GetComponent<BatteryComponent>(cell);
            crank = entities.GetComponent<CrankChargeComponent>(gun);
            Assert.That(crank.MaxCharges, Is.EqualTo(charges));
            Assert.That(crank.CrankDelay.TotalSeconds * charges, Is.EqualTo(seconds).Within(0.00001));

            wind = entities.System<SharedVerbSystem>()
                .GetLocalVerbs(gun, user, typeof(AlternativeVerb), force: true).First().Act!;
            startingEnergy = battery.CurrentCharge;
        });

        for (var i = 0; i < charges; i++)
        {
            await pair.Server.WaitAssertion(() => wind());
            await pair.RunTicksSync(pair.SecondsToTicks((float) crank.CrankDelay.TotalSeconds) + 2);
        }

        await pair.Server.WaitAssertion(() =>
        {
            remainingEnergy = battery.CurrentCharge;
            Assert.That(remainingEnergy, Is.LessThan(startingEnergy));
            Assert.That(crank.Charges, Is.EqualTo(charges));
            Assert.That(crank.NextChargeLoss, Is.InRange(timing.CurTime + TimeSpan.FromSeconds(7.7),
                timing.CurTime + TimeSpan.FromSeconds(8)));
            firstLoss = crank.NextChargeLoss!.Value;
            Assert.That(entities.System<SharedHandsSystem>().TryDrop(user), Is.True);
        });

        for (var i = 0; i < charges; i++)
        {
            var due = firstLoss + TimeSpan.FromSeconds(i);
            await pair.RunTicksSync((int) Math.Floor((due - timing.CurTime).TotalSeconds * timing.TickRate) - 1);
            await pair.Server.WaitAssertion(() => Assert.That(crank.Charges, Is.EqualTo(charges - i)));
            await pair.RunTicksSync(3);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(crank.Charges, Is.EqualTo(charges - i - 1));
                Assert.That(battery.CurrentCharge, Is.EqualTo(remainingEnergy));
            });
        }

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(crank.NextChargeLoss, Is.Null);
            entities.DeleteEntity(gun);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RecrankingAcrossDecayDeadlineRestartsGrace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        EntityUid gun = default, user = default;
        CrankChargeComponent crank = default!;
        TimeSpan started = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            gun = entities.SpawnEntity("MinutemenWeaponLaserMusket", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            crank = entities.GetComponent<CrankChargeComponent>(gun);
            crank.Charges = 2;
            started = timing.CurTime;
            crank.NextChargeLoss = started + TimeSpan.FromSeconds(0.1);
            entities.System<SharedVerbSystem>().GetLocalVerbs(gun, user, typeof(AlternativeVerb), force: true)
                .First().Act!.Invoke();
        });
        await pair.RunTicksSync(timing.TickRate / 3);
        await pair.Server.WaitAssertion(() => Assert.That(crank.Charges, Is.EqualTo(2)));
        await pair.RunTicksSync(timing.TickRate);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(crank.Charges, Is.EqualTo(3));
            Assert.That(crank.NextChargeLoss, Is.InRange(started + TimeSpan.FromSeconds(8) + crank.CrankDelay,
                timing.CurTime + TimeSpan.FromSeconds(8)));
            var shot = new GunShotEvent(user, new());
            entities.EventBus.RaiseLocalEvent(gun, ref shot);
            Assert.That(crank.Charges, Is.Zero);
            Assert.That(crank.NextChargeLoss, Is.Null);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }
}
