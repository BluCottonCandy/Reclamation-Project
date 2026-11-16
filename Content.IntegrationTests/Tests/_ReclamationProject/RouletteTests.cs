using System.Collections.Generic;
using System.Linq;
using Content.Server._ReclamationProject.Triggermen;
using Content.Shared._ReclamationProject.Triggermen;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class RouletteTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: TriggermenRouletteTable
  id: RouletteTestFundedTable
  components:
  - type: GamblingMachine
    bank: 10000
";

    [Test]
    public void EveryBetHasTheAdvertisedOddsAndBoundedProfit()
    {
        Assert.That(RouletteRules.WheelOrder, Is.EquivalentTo(Enumerable.Range(0, 38)));
        foreach (var kind in Enum.GetValues<RouletteBetKind>())
        foreach (var choice in kind == RouletteBetKind.Number ? Enumerable.Range(0, 38) : new[] { 0 })
        {
            var wins = Enumerable.Range(0, 38).Count(result => RouletteRules.Wins(kind, choice, result));
            var multiplier = RouletteRules.Multiplier(kind);
            Assert.That(wins, Is.EqualTo(kind == RouletteBetKind.Number ? 1 : multiplier == 3 ? 12 : 18));
            Assert.That(wins * multiplier, Is.EqualTo(36), "Every stake has the same 36/38 expected return.");
            Assert.That(RouletteRules.MaxBet(kind) * (multiplier - 1), Is.LessThanOrEqualTo(35));
            Assert.That(RouletteRules.IsValid(kind, choice, RouletteRules.MaxBet(kind)), Is.True);
            Assert.That(RouletteRules.IsValid(kind, choice, RouletteRules.MaxBet(kind) + 1), Is.False);
            Assert.That(RouletteRules.IsValid(kind, choice, 0), Is.False);
            if (kind != RouletteBetKind.Number)
            {
                Assert.That(RouletteRules.Wins(kind, choice, 0), Is.False);
                Assert.That(RouletteRules.Wins(kind, choice, 37), Is.False);
            }
        }
        Assert.That(RouletteRules.FormatNumber(37), Is.EqualTo("00"));
        Assert.That(RouletteRules.IsValid((RouletteBetKind) 255, 0, 1), Is.False);
        Assert.That(RouletteRules.IsValid(RouletteBetKind.Number, -1, 1), Is.False);
        Assert.That(RouletteRules.IsValid(RouletteBetKind.Number, 38, 1), Is.False);
    }

    [Test]
    public async Task InvalidUnfundedAndUnpaidBetsNeverChargeCaps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var caps = entities.SpawnEntity("TriggermenDonStartingCaps", map.GridCoords);
            var hands = entities.System<SharedHandsSystem>();
            var stacks = entities.System<SharedStackSystem>();
            Assert.That(hands.TryPickupAnyHand(user, caps), Is.True);
            var table = entities.SpawnEntity("TriggermenRouletteTable", map.GridCoords);
            var machine = entities.GetComponent<GamblingMachineComponent>(table);
            var roulette = entities.GetComponent<ReclamationRouletteTableComponent>(table);

            void Reject(RouletteBetKind kind, int choice, int amount, int bank)
            {
                machine.Bank = bank;
                var held = stacks.GetCount(caps);
                entities.EventBus.RaiseLocalEvent(table, new RouletteBet(kind, choice, amount) { Actor = user, UiKey = RouletteKey.Key });
                Assert.Multiple(() =>
                {
                    Assert.That(stacks.GetCount(caps), Is.EqualTo(held));
                    Assert.That(machine.Bank, Is.EqualTo(bank));
                    Assert.That(roulette.Player, Is.Null);
                    Assert.That(machine.RouletteSpinning, Is.False);
                    Assert.That(roulette.SpinId, Is.Zero);
                });
            }

            Reject(RouletteBetKind.Red, 0, 0, 1000);
            Reject(RouletteBetKind.Red, 0, -1, 1000);
            Reject(RouletteBetKind.Red, 0, int.MaxValue, 1000);
            Reject(RouletteBetKind.Number, 38, 1, 1000);
            Reject(RouletteBetKind.Number, -1, 1, 1000);
            Reject((RouletteBetKind) 255, 0, 1, 1000);
            Reject(RouletteBetKind.FirstDozen, 0, 6, 1000);
            Reject(RouletteBetKind.Number, 37, 2, 1000);
            Reject(RouletteBetKind.Number, 37, 1, 34); // Needs 35 house caps plus the stake.
            Reject(RouletteBetKind.Red, 0, 10, 9);
            Reject(RouletteBetKind.Red, 0, 1, int.MaxValue);
            stacks.SetCount(caps, 1);
            Reject(RouletteBetKind.Red, 0, 10, 1000);
            entities.DeleteEntity(user);
            entities.DeleteEntity(table);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PaidSpinSurvivesWindowCloseHidesResultAndSettlesExactlyOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var caps = entities.SpawnEntity("TriggermenDonStartingCaps", map.GridCoords);
            var hands = entities.System<SharedHandsSystem>();
            var stacks = entities.System<SharedStackSystem>();
            var ui = entities.System<SharedUserInterfaceSystem>();
            var system = entities.System<RouletteTableSystem>();
            Assert.That(hands.TryPickupAnyHand(user, caps), Is.True);
            var table = entities.SpawnEntity("RouletteTestFundedTable", map.GridCoords);
            var machine = entities.GetComponent<GamblingMachineComponent>(table);
            var roulette = entities.GetComponent<ReclamationRouletteTableComponent>(table);
            Assert.That(machine.Bank, Is.EqualTo(10000));

            int HeldCaps() => hands.EnumerateHeld(user).Where(item => entities.TryGetComponent<StackComponent>(item, out var stack) && stack.StackTypeId.Id == "Caps")
                .Sum(item => stacks.GetCount(item));
            int AllCaps() => entities.EntityQuery<StackComponent>()
                .Where(stack => stack.StackTypeId.Id == "Caps").Sum(stack => stack.Count);
            void Move(RouletteBetKind kind, int number, int amount)
                => entities.EventBus.RaiseLocalEvent(table, new RouletteBet(kind, number, amount) { Actor = user, UiKey = RouletteKey.Key });

            // Both green pockets, outside losses, an even-money win and a dozen win.
            foreach (var (kind, choice, amount, landed, returned) in new[]
                     {
                         (RouletteBetKind.Number, 0, 1, 0, 36),
                         (RouletteBetKind.Number, 37, 1, 37, 36),
                         (RouletteBetKind.Red, 0, 10, 0, 0),
                         (RouletteBetKind.Even, 0, 10, 37, 0),
                         (RouletteBetKind.Black, 0, 10, 2, 20),
                         (RouletteBetKind.SecondDozen, 0, 5, 13, 15),
                         (RouletteBetKind.Number, 1, 1, 2, 0)
                     })
            {
                var balance = machine.Bank;
                var held = HeldCaps();
                var totalCaps = AllCaps();
                var handsFull = hands.EnumerateHeld(user).Count() == 2;
                Move(kind, choice, amount);
                Assert.That(roulette.Player, Is.EqualTo(user));
                Assert.That(machine.RouletteSpinning, Is.True);
                Assert.That(roulette.PendingNumber, Is.InRange(0, 37));
                Assert.That(machine.Bank, Is.EqualTo(balance + amount));
                Assert.That(HeldCaps(), Is.EqualTo(held - amount));
                Assert.That(ui.TryGetUiState<RouletteState>(table, RouletteKey.Key, out var state), Is.True);
                Assert.That(state!.Spinning, Is.True);
                Assert.That(state.Result, Is.EqualTo(-1), "Pending result must not be sent to clients.");

                var spin = roulette.SpinId;
                var pending = roulette.PendingNumber;
                Move(kind, choice, amount); // A double-click must not charge, reroll, or restart the timer.
                Assert.That(roulette.SpinId, Is.EqualTo(spin));
                Assert.That(roulette.PendingNumber, Is.EqualTo(pending));
                Assert.That(HeldCaps(), Is.EqualTo(held - amount));
                entities.EventBus.RaiseLocalEvent(table, new BoundUIClosedEvent(RouletteKey.Key, table, user));
                Assert.That(roulette.Player, Is.EqualTo(user));

                // Deterministic settlement cases; live selection is always a uniform server roll.
                roulette.PendingNumber = landed;
                roulette.FinishAt = TimeSpan.Zero;
                system.Update(0.01f);
                Assert.That(machine.Bank, Is.EqualTo(balance + amount - returned));
                Assert.That(AllCaps(), Is.EqualTo(totalCaps - amount + returned));
                Assert.That(machine.Bank + AllCaps(), Is.EqualTo(balance + totalCaps), "Caps are conserved between player, table and floor.");
                Assert.That(HeldCaps(), Is.EqualTo(held - amount + (handsFull ? 0 : returned)),
                    "A full-handed player's winnings stay on the ground at the table.");
                Assert.That(machine.RouletteSpinning, Is.False);
                Assert.That(roulette.Player, Is.Null);
                Assert.That(ui.TryGetUiState<RouletteState>(table, RouletteKey.Key, out state), Is.True);
                Assert.That(state!.Result, Is.EqualTo(landed));
                Assert.That(state.Spinning, Is.False);
                Assert.That(state.History[0], Is.EqualTo(landed));
                system.Update(0.01f);
                Assert.That(machine.Bank, Is.EqualTo(balance + amount - returned));
                Assert.That(AllCaps(), Is.EqualTo(totalCaps - amount + returned), "Settlement is exactly once.");
            }
            entities.DeleteEntity(user);
            entities.DeleteEntity(table);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpinsVaryAndADeletedPlayerStillLeavesWinningCapsAtTheTable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var random = pair.Server.ResolveDependency<IRobustRandom>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var caps = entities.SpawnEntity("TriggermenDonStartingCaps", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, caps), Is.True);
            var table = entities.SpawnEntity("RouletteTestFundedTable", map.GridCoords);
            var machine = entities.GetComponent<GamblingMachineComponent>(table);
            var roulette = entities.GetComponent<ReclamationRouletteTableComponent>(table);
            var system = entities.System<RouletteTableSystem>();
            var outcomes = new HashSet<int>();
            for (var seed = 0; seed < 32; seed++)
            {
                random.SetSeed(seed);
                entities.EventBus.RaiseLocalEvent(table, new RouletteBet(RouletteBetKind.Red, 0, 1) { Actor = user, UiKey = RouletteKey.Key });
                Assert.That(roulette.Player, Is.EqualTo(user));
                outcomes.Add(roulette.PendingNumber);
                roulette.FinishAt = TimeSpan.Zero;
                system.Update(0.01f);
            }
            Assert.That(outcomes.Count, Is.GreaterThan(10));
            Assert.That(roulette.History.Count, Is.EqualTo(12));
            machine.Bank = 35; // Minimum funds for a straight-number bet.
            entities.EventBus.RaiseLocalEvent(table, new RouletteBet(RouletteBetKind.Number, 37, 1) { Actor = user, UiKey = RouletteKey.Key });
            Assert.That(roulette.Player, Is.EqualTo(user));
            entities.DeleteEntity(user);
            roulette.PendingNumber = 37;
            roulette.FinishAt = TimeSpan.Zero;
            system.Update(0.01f);
            Assert.That(machine.Bank, Is.Zero);
            Assert.That(roulette.Player, Is.Null);
            var payout = entities.EntityQuery<StackComponent>().Where(stack => stack.StackTypeId.Id == "Caps" && stack.Count == 36).ToArray();
            Assert.That(payout.Length, Is.EqualTo(1), "Disconnected/deleted player does not destroy the payout.");
            entities.DeleteEntity(table);
        });
        await pair.CleanReturnAsync();
    }
}
