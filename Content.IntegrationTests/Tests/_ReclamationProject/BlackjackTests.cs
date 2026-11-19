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
public sealed class BlackjackTests
{
    [Test]
    public async Task EveryHandUsesOneShuffledDeckForBothPlayers()
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
            var table = entities.SpawnEntity("TriggermenBlackjackTable", map.GridCoords);
            var machine = entities.GetComponent<GamblingMachineComponent>(table);
            machine.Bank = 10000;
            var openings = new HashSet<string>();
            var hits = 0;

            void Move(int amount, bool hold = false)
                => entities.EventBus.RaiseLocalEvent(table, new BlackjackMove(amount, hold) { Actor = user, UiKey = BlackjackKey.Key });

            void CheckDeck()
            {
                // Also catches cards lost or duplicated between the deal, hits,
                // dealer draws and the undealt portion of the same deck.
                var cards = machine.PlayerCards.Concat(machine.DealerCards).Concat(machine.BlackjackDeck).ToArray();
                Assert.That(cards, Is.EquivalentTo(Enumerable.Range(0, 52)));
            }

            for (var seed = 0; seed < 32; seed++)
            {
                // Reproducible test data only; live play never resets the RNG.
                random.SetSeed(seed);
                Move(1);
                openings.Add(string.Join(",", machine.PlayerCards.Concat(machine.DealerCards.Take(2))));
                CheckDeck();
                if (machine.BlackjackPlayer == user)
                {
                    var next = machine.BlackjackDeck.Peek();
                    Move(0);
                    Assert.That(machine.PlayerCards[^1], Is.EqualTo(next), "Hit must take the next undealt card.");
                    hits++;
                    CheckDeck();
                    if (machine.BlackjackPlayer == user)
                        Move(0, true);
                }

                Assert.That(machine.BlackjackPlayer, Is.Null);
                CheckDeck();
            }

            Assert.That(openings.Count, Is.GreaterThan(1), "The shuffled opening must not be fixed.");
            Assert.That(hits, Is.GreaterThan(0));
            entities.DeleteEntity(user);
            entities.DeleteEntity(table);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DealerDrawsFromTheRemainingDeckAndPaysForTheActualResult()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var table = entities.SpawnEntity("TriggermenBlackjackTable", map.GridCoords);
            var machine = entities.GetComponent<GamblingMachineComponent>(table);
            var stacks = entities.System<SharedStackSystem>();
            var hands = entities.System<SharedHandsSystem>();

            int HeldCaps() => hands.EnumerateHeld(user)
                .Where(item => entities.TryGetComponent<StackComponent>(item, out var stack) && stack.StackTypeId.Id == "Caps")
                .Sum(item => stacks.GetCount(item));

            // Fixed post-deal hands isolate the settlement rules from the shuffle:
            // player 18 / dealer 17 wins; 16 / 17 loses; 17 / 17 pushes.
            foreach (var (secondCard, payout) in new[] { (7, 20), (5, 0), (6, 10) })
            {
                machine.Bank = 1010; // 1000 house funds + the already-paid 10-cap bet.
                machine.BlackjackBet = 10;
                machine.BlackjackPlayer = user;
                machine.PlayerCards = [9, secondCard];
                machine.DealerCards = [8, 4];
                machine.BlackjackDeck = new Queue<int>([2, 12]);
                var before = HeldCaps();
                entities.EventBus.RaiseLocalEvent(table, new BlackjackMove(0, true) { Actor = user, UiKey = BlackjackKey.Key });
                Assert.Multiple(() =>
                {
                    Assert.That(machine.DealerCards, Is.EqualTo(new[] { 8, 4, 2 }));
                    Assert.That(machine.BlackjackDeck, Is.EqualTo(new[] { 12 }), "Dealer must stop at 17.");
                    Assert.That(machine.Bank, Is.EqualTo(1010 - payout));
                    Assert.That(HeldCaps(), Is.EqualTo(before + payout));
                    Assert.That(machine.BlackjackPlayer, Is.Null);
                });
            }

            entities.DeleteEntity(user);
            entities.DeleteEntity(table);
        });
        await pair.CleanReturnAsync();
    }
}
