using Content.Shared._Misfits.Mobs;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AwakeMobPredictionTest
{
    [Test]
    public async Task AwakeMarkerIsOnlyChangedByTheServer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var serverEntities = pair.Server.ResolveDependency<IEntityManager>();
        var clientEntities = pair.Client.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var mob = serverEntities.SpawnEntity(null, MapCoordinates.Nullspace);
            var state = serverEntities.AddComponent<MobStateComponent>(mob);
            serverEntities.EventBus.RaiseLocalEvent(mob,
                new MobStateChangedEvent(mob, state, MobState.Critical, MobState.Alive), true);
            Assert.That(serverEntities.HasComponent<AwakeMobComponent>(mob), Is.True);
            serverEntities.EventBus.RaiseLocalEvent(mob,
                new MobStateChangedEvent(mob, state, MobState.Alive, MobState.Critical), true);
            Assert.That(serverEntities.HasComponent<AwakeMobComponent>(mob), Is.False);
            serverEntities.DeleteEntity(mob);
        });

        await pair.Client.WaitAssertion(() =>
        {
            var mob = clientEntities.SpawnEntity(null, MapCoordinates.Nullspace);
            var state = clientEntities.AddComponent<MobStateComponent>(mob);
            // This same event is raised while restoring damage during prediction rollback.
            clientEntities.EventBus.RaiseLocalEvent(mob,
                new MobStateChangedEvent(mob, state, MobState.Critical, MobState.Alive), true);
            Assert.That(clientEntities.HasComponent<AwakeMobComponent>(mob), Is.False);
            // A marker received from the server must also survive a predicted critical state.
            clientEntities.AddComponent<AwakeMobComponent>(mob);
            clientEntities.EventBus.RaiseLocalEvent(mob,
                new MobStateChangedEvent(mob, state, MobState.Alive, MobState.Critical), true);
            Assert.That(clientEntities.HasComponent<AwakeMobComponent>(mob), Is.True);
            clientEntities.DeleteEntity(mob);
        });
        await pair.CleanReturnAsync();
    }
}
