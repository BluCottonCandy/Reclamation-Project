using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests;

[TestFixture, NonParallelizable]
// Minimal organs and explicit due times isolate digestion from unrelated metabolism.
#pragma warning disable RA0002
public sealed class IdleDigestionTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: IdleDigestionTestBody
  components:
  - type: SolutionContainerManager
    solutions:
      chemicals:
        maxVol: 1000

- type: entity
  id: IdleDigestionTestStomach
  components:
  - type: Organ
  - type: Stomach
    digestionDelay: 2
  - type: SolutionContainerManager
    solutions:
      stomach:
        maxVol: 100
""";

    [TestCase("Water")]
    [TestCase("Bicaridine")]
    [TestCase("Toxin")]
    public async Task IdleStomachsWakeWithoutCatchingUpAndTransferTheSameDose(string reagent)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var system = em.System<StomachSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid organ = default, body = default;
        MapId map = default;
        TimeSpan idleDue = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out map, runMapInit: true);
            body = em.SpawnEntity("IdleDigestionTestBody", new MapCoordinates(0, 0, map));
            organ = em.SpawnEntity("IdleDigestionTestStomach", new MapCoordinates(0, 0, map));
            em.GetComponent<OrganComponent>(organ).Body = body;
            var stomach = em.GetComponent<StomachComponent>(organ);
            stomach.NextUpdate = timing.CurTime;
            system.Update(0);
            idleDue = stomach.NextUpdate;
        });
        try
        {
            await pair.RunSeconds(6);
            await server.WaitAssertion(() =>
            {
                var stomach = em.GetComponent<StomachComponent>(organ);
                Assert.That(stomach.NextUpdate, Is.EqualTo(idleDue), "empty stomachs leave the update loop");
                Assert.That(system.TryTransferSolution(organ, new Solution(reagent, 5)), Is.True);
                Assert.That(stomach.NextUpdate, Is.GreaterThan(timing.CurTime - timing.TickPeriod));
                Assert.That(stomach.NextUpdate, Is.LessThanOrEqualTo(timing.CurTime + stomach.UpdateInterval));
                Assert.That((stomach.NextUpdate - idleDue).Ticks % stomach.UpdateInterval.Ticks, Is.Zero,
                    "waking preserves the original digestion phase");
                var solutions = em.System<SolutionContainerSystem>();
                Assert.That(solutions.TryGetSolution(body, "chemicals", out _, out var blood), Is.True);
                for (var i = 1; i <= 2; i++)
                {
                    stomach.NextUpdate = timing.CurTime;
                    system.Update(0);
                    Assert.That(stomach.ReagentDeltas[0].Lifetime, Is.EqualTo(TimeSpan.FromSeconds(i)));
                    Assert.That(blood!.Volume.Float(), Is.Zero, "transfer requires strictly more than the delay");
                    system.Update(0);
                    Assert.That(stomach.ReagentDeltas[0].Lifetime, Is.EqualTo(TimeSpan.FromSeconds(i)),
                        "no catch-up digestion between scheduled intervals");
                }
                stomach.NextUpdate = timing.CurTime;
                system.Update(0);
                Assert.That(blood!.GetTotalPrototypeQuantity(reagent).Float(), Is.EqualTo(5));
                Assert.That(stomach.ReagentDeltas, Is.Empty);
                idleDue = stomach.NextUpdate;
            });
            await pair.RunSeconds(3);
            await server.WaitAssertion(() => Assert.That(em.GetComponent<StomachComponent>(organ).NextUpdate,
                Is.EqualTo(idleDue), "finished digestion returns to idle"));
        }
        finally { await server.WaitPost(() => maps.DeleteMap(map)); }
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PauseDoesNotDigestOrDoubleShiftMealsAddedWhilePaused(bool eatBeforePause)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var system = em.System<StomachSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid organ = default;
        MapId map = default;
        TimeSpan pauseStart = default, due = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out map, runMapInit: true);
            var body = em.SpawnEntity("IdleDigestionTestBody", new MapCoordinates(0, 0, map));
            organ = em.SpawnEntity("IdleDigestionTestStomach", new MapCoordinates(0, 0, map));
            em.GetComponent<OrganComponent>(organ).Body = body;
            em.GetComponent<StomachComponent>(organ).NextUpdate = timing.CurTime;
            system.Update(0);
        });
        try
        {
            await pair.RunSeconds(4);
            await server.WaitAssertion(() =>
            {
                if (eatBeforePause)
                    Assert.That(system.TryTransferSolution(organ, new Solution("Water", 5)), Is.True);
                maps.SetPaused(map, true);
                pauseStart = timing.CurTime;
                due = em.GetComponent<StomachComponent>(organ).NextUpdate;
            });
            await pair.RunSeconds(5);
            await server.WaitAssertion(() =>
            {
                if (!eatBeforePause)
                    Assert.That(system.TryTransferSolution(organ, new Solution("Water", 5)), Is.True);
                var stomach = em.GetComponent<StomachComponent>(organ);
                var slow = new ApplyMetabolicMultiplierEvent(organ, 2, true);
                em.EventBus.RaiseLocalEvent(organ, ref slow);
                Assert.That(stomach.NextUpdate, Is.EqualTo(due), "paused wake and multiplier must not advance the clock");
                system.Update(0);
                Assert.That(stomach.ReagentDeltas[0].Lifetime, Is.EqualTo(TimeSpan.Zero));
                maps.SetPaused(map, false);
                Assert.That(stomach.NextUpdate, Is.EqualTo(due + timing.CurTime - pauseStart));
                stomach.NextUpdate = timing.CurTime;
                system.Update(0);
                Assert.That(stomach.ReagentDeltas[0].Lifetime, Is.EqualTo(TimeSpan.FromSeconds(2)));
                var restore = new ApplyMetabolicMultiplierEvent(organ, 2, false);
                em.EventBus.RaiseLocalEvent(organ, ref restore);
                Assert.That(stomach.UpdateInterval, Is.EqualTo(TimeSpan.FromSeconds(1)));
            });
        }
        finally { await server.WaitPost(() => maps.DeleteMap(map)); }
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IdleMetabolismChangesPreserveTheNextDueTimeAndExternalSolutionsWake()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var system = em.System<StomachSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid organ = default;
        MapId map = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out map, runMapInit: true);
            organ = em.SpawnEntity("IdleDigestionTestStomach", new MapCoordinates(0, 0, map));
            em.GetComponent<StomachComponent>(organ).NextUpdate = timing.CurTime;
            system.Update(0);
        });
        try
        {
            await pair.RunSeconds(6);
            await server.WaitAssertion(() =>
            {
                var stomach = em.GetComponent<StomachComponent>(organ);
                var slow = new ApplyMetabolicMultiplierEvent(organ, 2, true);
                em.EventBus.RaiseLocalEvent(organ, ref slow);
                var due = stomach.NextUpdate;
                Assert.That(due, Is.GreaterThan(timing.CurTime - timing.TickPeriod));
                Assert.That(due, Is.LessThanOrEqualTo(timing.CurTime + TimeSpan.FromSeconds(1)),
                    "existing due time is normalized with the old interval");
                Assert.That(stomach.UpdateInterval, Is.EqualTo(TimeSpan.FromSeconds(2)));
                var solutions = em.System<SolutionContainerSystem>();
                Assert.That(solutions.TryGetSolution(organ, "stomach", out var solution), Is.True);
                Assert.That(solutions.TryAddSolution(solution!.Value, new Solution("Water", 5)), Is.True);
                Assert.That(stomach.NextUpdate, Is.EqualTo(due));
                stomach.NextUpdate = timing.CurTime;
                system.Update(0);
                Assert.That(stomach.NextUpdate, Is.EqualTo(timing.CurTime + TimeSpan.FromSeconds(2)),
                    "external solution changes wake detached stomachs too");
                Assert.That(stomach.ReagentDeltas, Is.Empty, "raw/restored contents must not invent ingestion records");
                var restore = new ApplyMetabolicMultiplierEvent(organ, 2, false);
                em.EventBus.RaiseLocalEvent(organ, ref restore);
                Assert.That(stomach.UpdateInterval, Is.EqualTo(TimeSpan.FromSeconds(1)));
            });
        }
        finally { await server.WaitPost(() => maps.DeleteMap(map)); }
        await pair.CleanReturnAsync();
    }
}
#pragma warning restore RA0002
