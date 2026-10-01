// #Misfits Add - Integration test: verifies N14CryoBunkerLadder frees a job slot when a player is cryo'd

using System;
using Content.IntegrationTests.Pair;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared.Bed.Cryostorage;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Misfits;

/// <summary>
/// Verifies that when a player entity is placed into an <see cref="N14CryoBunkerLadder"/> and the
/// cryo grace period expires, the job slot for that player is correctly freed on the station.
/// </summary>
[TestFixture]
public sealed class CryoBunkerLadderJobSlotTest
{
    // Minimal inline prototypes for the test: a single finite-slot job and a station that tracks it.
    [TestPrototypes]
    private const string Prototypes = @"
- type: playTimeTracker
  id: CryoSlotTestJobTracker

- type: job
  id: CryoSlotTestJob
  playTimeTracker: CryoSlotTestJobTracker

- type: gameMap
  id: CryoSlotTestStation
  minPlayers: 0
  mapName: CryoSlotTestStation
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: CryoSlotTestStation
      stationProto: StandardNanotrasenStation
      components:
        - type: StationJobs
          overflowJobs: []
          availableJobs:
            CryoSlotTestJob: [1, 1]
";

    /// <summary>
    /// Places a dummy entity into an <see cref="N14CryoBunkerLadder"/>, simulates an expired grace
    /// period, and asserts that the job slot previously held by that entity is returned to the station.
    /// </summary>
    [Test]
    public async Task CryoBunkerLadder_FreesJobSlot_WhenGracePeriodExpires()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;

        var entMan      = server.ResolveDependency<IEntityManager>();
        var protoMan    = server.ResolveDependency<IPrototypeManager>();
        var stationSys  = entMan.System<StationSystem>();
        var jobsSys     = entMan.System<StationJobsSystem>();
        var contSys     = entMan.System<SharedContainerSystem>();

        // Create a map so entity positions resolve correctly.
        var map = await pair.CreateTestMap();

        var stationProto = protoMan.Index<GameMapPrototype>("CryoSlotTestStation");

        // A fake NetUserId that stands in for a real player session.
        var fakeUserId = new NetUserId(Guid.NewGuid());

        EntityUid station      = default;
        EntityUid cryoLadder   = default;
        EntityUid playerEntity = default;

        await server.WaitPost(() =>
        {
            // — Station setup —
            station = stationSys.InitializeNewStation(
                stationProto.Stations["Station"], null, "CryoSlotTest");

            // — Entity setup —
            // Spawn the bunker hatch on the test map.
            cryoLadder   = entMan.SpawnEntity("N14CryoBunkerLadder", map.MapCoords);

            // Spawn a generic entity to represent the player body.
            playerEntity = entMan.SpawnEntity(null, map.MapCoords);

            // — Job assignment —
            // TryAssignJob decrements the slot from 1 → 0 and records the user in PlayerJobs.
            Assert.That(
                jobsSys.TryAssignJob(station, "CryoSlotTestJob", fakeUserId),
                Is.True,
                "TryAssignJob should succeed when a slot is available.");

            jobsSys.TryGetJobSlot(station, "CryoSlotTestJob", out var slotsBefore);
            Assert.That(slotsBefore, Is.EqualTo(0),
                "Slot count should be 0 after the job is assigned.");

            // — Cryo insertion —
            // Force-insert bypasses CanEnterCryostorageComponent / MindContainerComponent checks,
            // but OnInsertedContainer still fires and adds CryostorageContainedComponent.
            var cryoContainer = contSys.GetContainer(cryoLadder, "storage");
            Assert.That(
                contSys.Insert(playerEntity, cryoContainer, force: true),
                Is.True,
                "Force-insert into the cryostorage container should succeed.");

            // At this point OnInsertedContainer has set GracePeriodEndTime and Cryostorage.
            // Override them: expire the grace period immediately and assign the fake user.
            var contained = entMan.GetComponent<CryostorageContainedComponent>(playerEntity);
            contained.UserId           = fakeUserId;
            contained.GracePeriodEndTime = TimeSpan.Zero; // Always < CurTime → fires on next Update
        });

        // Let the CryostorageSystem.Update tick process the expired grace period.
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // Job slot should have been incremented back to 1.
            Assert.That(
                jobsSys.TryGetJobSlot(station, "CryoSlotTestJob", out var slotsAfter),
                Is.True,
                "TryGetJobSlot should still resolve after cryo processing.");

            Assert.That(slotsAfter, Is.EqualTo(1),
                "Job slot should be restored to 1 after the cryo grace period expires.");

            // The user should no longer appear in the station's PlayerJobs table.
            Assert.That(
                jobsSys.TryGetPlayerJobs(station, fakeUserId, out _),
                Is.False,
                "Player should be removed from station PlayerJobs after entering cryostorage.");
        });

        await pair.CleanReturnAsync();
    }
}
