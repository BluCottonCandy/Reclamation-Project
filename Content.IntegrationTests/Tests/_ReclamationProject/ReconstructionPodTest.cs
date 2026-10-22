using Content.Server._Reclamation.Reconstruction;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class ReconstructionPodTest
{
    [Test]
    public async Task PodHoldsPatientAndDropsThemWhenDestroyed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var containers = entities.System<SharedContainerSystem>();
            var pod = entities.SpawnEntity("RailroadBiosculptor", map.GridCoords);
            var patient = entities.SpawnEntity("MobHuman", map.GridCoords);

            var comp = entities.GetComponent<ReconstructionPodComponent>(pod);
            Assert.That(containers.TryGetContainer(pod, comp.ContainerId, out var container), Is.True);
            Assert.That(containers.Insert(patient, container!), Is.True);
            Assert.That(containers.IsEntityInContainer(patient), Is.True);

            entities.DeleteEntity(pod);
            Assert.That(entities.EntityExists(patient), Is.True, "destroying the pod deleted the patient");
            Assert.That(containers.IsEntityInContainer(patient), Is.False);
            entities.DeleteEntity(patient);
        });

        await pair.CleanReturnAsync();
    }
}
