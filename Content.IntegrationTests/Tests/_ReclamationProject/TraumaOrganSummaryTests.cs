using Content.Server._ReclamationProject.Medical;
using Content.Server.Body.Components;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class TraumaOrganSummaryTests
{
    [Test]
    public async Task RepeatedSummariesKeepIndependentKindsAndPermanentHeartDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            try
            {
                var body = em.System<SharedBodySystem>();
                var trauma = em.System<TraumaSystem>();
                var medical = em.GetComponent<TraumaBodyComponent>(patient);
                var organs = body.GetBodyOrgans(patient)
                    .Where(o => em.HasComponent<TraumaOrganComponent>(o.Id))
                    .ToDictionary(o => em.GetComponent<TraumaOrganComponent>(o.Id).Kind, o => o.Id);
                var stamina = em.GetComponent<StaminaComponent>(patient);
                var capacity = stamina.CritThreshold;
                var decay = stamina.Decay;
                var thresholds = em.System<MobThresholdSystem>();
                var critical = thresholds.GetThresholdForState(patient, MobState.Critical);
                foreach (var (kind, damage) in new[] { (TraumaKind.Lungs, 40f), (TraumaKind.Liver, 60f),
                             (TraumaKind.Kidneys, 20f), (TraumaKind.Eyes, 10f), (TraumaKind.Stomach, 80f) })
                    em.GetComponent<TraumaOrganComponent>(organs[kind]).Damage = damage;
                var heart = em.GetComponent<TraumaOrganComponent>(organs[TraumaKind.Heart]);
                heart.MaximumHealth = 60;
                heart.Damage = 10;
                trauma.Refresh(patient, medical);
                var liver = em.GetComponent<MetabolizerComponent>(organs[TraumaKind.Liver]);
                var interval = liver.UpdateInterval;
                for (var i = 0; i < 100; i++) trauma.Refresh(patient, medical);
                Assert.Multiple(() =>
                {
                    Assert.That(medical.LungDamage, Is.EqualTo(40));
                    Assert.That(medical.HeartDamage, Is.EqualTo(50), "permanent loss contributes even after repairing injury");
                    Assert.That(medical.LiverDamage, Is.EqualTo(60).Within(.001f));
                    Assert.That(medical.KidneyDamage, Is.EqualTo(20));
                    Assert.That(medical.EyeDamage, Is.EqualTo(10));
                    Assert.That(medical.StomachDamage, Is.EqualTo(80));
                    Assert.That(stamina.CritThreshold, Is.EqualTo(capacity * .6f).Within(.001f));
                    Assert.That(stamina.Decay, Is.EqualTo(decay * .9f).Within(.001f));
                    Assert.That(thresholds.GetThresholdForState(patient, MobState.Critical), Is.EqualTo(critical * .75f));
                    Assert.That(liver.UpdateInterval, Is.EqualTo(interval), "repeated summaries must not compound metabolism");
                });
                heart.Damage = 0;
                trauma.Refresh(patient, medical);
                Assert.That(medical.HeartDamage, Is.EqualTo(40), "healing does not undo permanent capacity loss");
            }
            finally { em.DeleteEntity(patient); }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SummariesAverageMultipleOrgansAndCountMissingOriginals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var donor = em.SpawnTraumaPatient(map.GridCoords);
            var body = em.System<SharedBodySystem>();
            var trauma = em.System<TraumaSystem>();
            var medical = em.GetComponent<TraumaBodyComponent>(patient);
            var lung = body.GetBodyOrgans(patient).First(o => em.TryGetComponent<TraumaOrganComponent>(o.Id, out var c) && c.Kind == TraumaKind.Lungs).Id;
            var extra = body.GetBodyOrgans(donor).First(o => em.TryGetComponent<TraumaOrganComponent>(o.Id, out var c) && c.Kind == TraumaKind.Lungs).Id;
            try
            {
                em.GetComponent<TraumaOrganComponent>(lung).Damage = 40;
                em.GetComponent<TraumaOrganComponent>(extra).Damage = 20;
                Assert.That(body.RemoveOrgan(extra), Is.True);
                var torso = body.GetBodyChildrenOfType(patient, BodyPartType.Torso).First().Id;
                Assert.That(body.TryCreateOrganSlot(torso, "test-lung", out _), Is.True);
                Assert.That(body.InsertOrgan(torso, extra, "test-lung"), Is.True);
                trauma.Refresh(patient, medical);
                Assert.That(medical.LungDamage, Is.EqualTo(30), "extra transplants contribute to the average");
                medical.OriginalOrgans[TraumaKind.Lungs] = 2;
                Assert.That(body.RemoveOrgan(extra), Is.True);
                trauma.Refresh(patient, medical);
                Assert.That(medical.LungDamage, Is.EqualTo(70), "a missing original counts as complete loss");
            }
            finally
            {
                em.DeleteEntity(patient);
                em.DeleteEntity(donor);
                if (em.EntityExists(extra)) em.DeleteEntity(extra);
            }
        });
        await pair.CleanReturnAsync();
    }
}
