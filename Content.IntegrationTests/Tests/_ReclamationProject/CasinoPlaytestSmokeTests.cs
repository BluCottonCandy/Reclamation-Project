using Content.Server._ReclamationProject.Triggermen;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class CasinoPlaytestSmokeTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: TriggermenGamblingMachine
  id: Lucky38PlaytestSlots
  name: Casino test - slots
  suffix: LOCAL PLAYTEST
  components:
  - type: GamblingMachine
    bank: 10000

- type: entity
  parent: TriggermenBlackjackTable
  id: Lucky38PlaytestBlackjack
  name: Casino test - blackjack
  suffix: LOCAL PLAYTEST
  components:
  - type: GamblingMachine
    bank: 10000

- type: entity
  parent: TriggermenPoolTable
  id: Lucky38PlaytestPool
  name: Casino test - pool
  suffix: LOCAL PLAYTEST

- type: entity
  parent: TriggermenDonStartingCaps
  id: Lucky38PlaytestCaps
  name: Casino test - caps
  suffix: LOCAL PLAYTEST, 1000
";

    [Test]
    public async Task LocalCasinoTestObjectsSpawnWithFundedBanks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var slots = entities.SpawnEntity("Lucky38PlaytestSlots", map.GridCoords);
            var blackjack = entities.SpawnEntity("Lucky38PlaytestBlackjack", map.GridCoords);
            var pool = entities.SpawnEntity("Lucky38PlaytestPool", map.GridCoords);
            var caps = entities.SpawnEntity("Lucky38PlaytestCaps", map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<GamblingMachineComponent>(slots).Bank, Is.EqualTo(10000));
                Assert.That(entities.GetComponent<GamblingMachineComponent>(slots).Blackjack, Is.False);
                Assert.That(entities.GetComponent<GamblingMachineComponent>(blackjack).Bank, Is.EqualTo(10000));
                Assert.That(entities.GetComponent<GamblingMachineComponent>(blackjack).Blackjack, Is.True);
                Assert.That(entities.HasComponent<PoolTableComponent>(pool), Is.True);
                Assert.That(entities.System<SharedStackSystem>().GetCount(caps), Is.EqualTo(1000));
            });
            entities.DeleteEntity(slots);
            entities.DeleteEntity(blackjack);
            entities.DeleteEntity(pool);
            entities.DeleteEntity(caps);
        });
        await pair.CleanReturnAsync();
    }
}
