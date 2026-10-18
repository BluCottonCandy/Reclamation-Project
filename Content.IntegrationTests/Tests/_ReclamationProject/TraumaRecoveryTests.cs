using System.Linq;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Bed.Components;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Buckle;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class TraumaRecoveryTests
{
    [TestCase(false, false, false, 1f)]
    [TestCase(true, false, false, 3f)]
    [TestCase(false, true, false, 5f)]
    [TestCase(false, true, true, 10f)]
    [TestCase(true, true, true, 10f)]
    public async Task RecoveryUsesBedSleepAndRegenerationAndStopsOnDeath(bool regen, bool bed, bool sleep, float rate)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var fixture = em.SpawnEntity("Bed", map.GridCoords);
            if (regen) em.EnsureComponent<TraumaRegenerationComponent>(patient);
            if (bed)
            {
                em.EnsureComponent<HealOnBuckleComponent>(fixture).Damage = new();
                Assert.That(em.System<SharedBuckleSystem>().TryBuckle(patient, patient, fixture), Is.True);
            }
            if (sleep) em.EnsureComponent<SleepingComponent>(patient);
            var body = em.System<SharedBodySystem>();
            var trauma = em.System<TraumaSystem>();
            var medical = em.GetComponent<TraumaBodyComponent>(patient);
            var lung = body.GetBodyOrgans(patient).First(o => em.TryGetComponent<TraumaOrganComponent>(o.Id, out var c) && c.Kind == TraumaKind.Lungs).Id;
            var arm = body.GetBodyChildrenOfType(patient, BodyPartType.Arm).First().Id;
            trauma.SetOrganDamage(lung, 50);
            trauma.Fracture(arm);
            trauma.AddInternalBleeding(patient, 4);
            trauma.HealNaturally(patient, medical, 60);
            Assert.That(em.GetComponent<TraumaOrganComponent>(lung).Damage, Is.EqualTo(50-rate).Within(.001));
            Assert.That(em.GetComponent<FractureComponent>(arm).HealingProgress, Is.EqualTo(rate).Within(.001));
            Assert.That(medical.InternalBleeding, Is.EqualTo(4-rate*.04f).Within(.001));
            em.System<MobStateSystem>().ChangeMobState(patient, MobState.Dead);
            trauma.HealNaturally(patient, medical, 60);
            Assert.That(em.GetComponent<TraumaOrganComponent>(lung).Damage, Is.EqualTo(50-rate).Within(.001));
            if (bed) em.System<SharedBuckleSystem>().TryUnbuckle(patient, null);
            em.DeleteEntity(patient);
            em.DeleteEntity(fixture);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NpcHumanoidsAndAnimalsDoNotGetTrauma()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var prototype in new[] { "MobHuman", "MobMonkey" })
            {
                var npc = em.SpawnEntity(prototype, map.GridCoords);
                Assert.That(em.HasComponent<TraumaBodyComponent>(npc), Is.False);
                foreach (var (part, _) in em.System<SharedBodySystem>().GetBodyChildren(npc))
                    Assert.That(em.HasComponent<TraumaLimbComponent>(part), Is.False);
                em.DeleteEntity(npc);
            }
        });
        await pair.CleanReturnAsync();
    }
}
