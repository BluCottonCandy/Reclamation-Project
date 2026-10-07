using System.Linq;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.Explosion.Components.OnTrigger;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class GhoulDeploymentGrenadeTest
{
    [Test]
    public async Task DeploysTwoGhoulsAndHarmlessFiveSecondSmoke()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid grenade = default;

        await pair.Server.WaitAssertion(() =>
        {
            grenade = entities.SpawnEntity("ReclamationGhoulDeploymentGrenade", map.GridCoords);
            Assert.That(entities.HasComponent<ExplosiveComponent>(grenade), Is.False);
            Assert.That(entities.HasComponent<ExplodeOnTriggerComponent>(grenade), Is.False);
            var smoke = entities.GetComponent<SmokeOnTriggerComponent>(grenade);
            Assert.That(smoke.Duration, Is.EqualTo(5));
            var solution = smoke.Solution;
            Assert.That(solution.Volume.Float(), Is.Zero);
            entities.System<TriggerSystem>().Trigger(grenade);
            Assert.That(entities.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == "N14MobGhoulFeral"), Is.EqualTo(2));
            Assert.That(entities.EntityQuery<SmokeComponent>().Any(), Is.True);
        });

        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(grenade), Is.True);
            Assert.That(entities.EntityQuery<MetaDataComponent>()
                .Any(meta => meta.EntityPrototype?.ID == "ReclamationGhoulDeploymentPayload"), Is.False);
        });
        await pair.RunSeconds(7);
        await pair.Server.WaitAssertion(() =>
            Assert.That(entities.EntityQuery<SmokeComponent>().Any(), Is.False));
        await pair.CleanReturnAsync();
    }
}
