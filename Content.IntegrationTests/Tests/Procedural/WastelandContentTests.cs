using System;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.StationEvents;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Procedural;

[TestFixture]
public sealed class WastelandContentTests
{
    [Test]
    public async Task WastelandRemovesNuclearEquipmentAndCancelsStationEvents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid[] equipment = [];
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var ticker = em.System<GameTicker>();
            // Do not rely on events.enabled being false: queued and manual events must be blocked too.
            pair.Server.ResolveDependency<IConfigurationManager>().SetCVar(CCVars.EventsEnabled, true);
            equipment = new[] { "NuclearBomb", "NuclearBombUnanchored", "NukeDisk", "NukeDiskFake" }
                .Select(id => em.SpawnEntity(id, map.GridCoords)).ToArray();
            var pending = em.SpawnEntity("UnknownShuttleDisasterEvacPod", MapCoordinates.Nullspace);
            em.EnsureComponent<DelayedStartRuleComponent>(pending).RuleStartTime = TimeSpan.MaxValue;
            var scheduler = ticker.AddGameRule("BasicStationEventScheduler");
            ticker.AddGameRule("N14Rule");
            Assert.That(em.HasComponent<EndedGameRuleComponent>(pending), Is.True);
            Assert.That(em.HasComponent<DelayedStartRuleComponent>(pending), Is.False);
            Assert.That(em.HasComponent<EndedGameRuleComponent>(scheduler), Is.True);
            foreach (var id in new[] { "UnknownShuttleDisasterEvacPod", "UnknownShuttleSyndieEvacPod",
                         "BasicStationEventScheduler", "RampingStationEventScheduler" })
            {
                Assert.That(ticker.StartGameRule(id, out var rule), Is.False, id);
                Assert.That(em.HasComponent<EndedGameRuleComponent>(rule), Is.True, id);
                Assert.That(em.HasComponent<DelayedStartRuleComponent>(rule), Is.False, id);
            }
            var oscillating = em.SpawnEntity("BasicStationEventScheduler", MapCoordinates.Nullspace);
            em.RemoveComponent<Content.Server.StationEvents.Components.BasicStationEventSchedulerComponent>(oscillating);
            em.EnsureComponent<Content.Server.StationEvents.Components.OscillatingStationEventSchedulerComponent>(oscillating);
            Assert.That(ticker.StartGameRule(oscillating), Is.False);
            Assert.That(em.HasComponent<EndedGameRuleComponent>(oscillating), Is.True);
            Assert.That(em.System<EventManagerSystem>().AllEvents(), Is.Empty);
        });
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            foreach (var uid in equipment)
                Assert.That(em.EntityExists(uid), Is.False);
            // Later map loads and replacement attempts must also be removed.
            equipment = new[] { "NuclearBomb", "NukeDisk" }
                .Select(id => em.SpawnEntity(id, map.GridCoords)).ToArray();
        });
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            foreach (var uid in equipment)
                Assert.That(em.EntityExists(uid), Is.False);
            var query = em.EntityQueryEnumerator<Content.Server._ReclamationProject.Rules.RemovedWastelandContentComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.False, "Disks must not respawn after deletion.");
        });
        await pair.CleanReturnAsync();
    }
}
