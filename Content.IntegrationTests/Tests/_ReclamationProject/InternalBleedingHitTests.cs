using System.Linq;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Body.Systems;
using Content.Server.Body.Components;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class InternalBleedingHitTests
{
    [Test]
    public void OrdinaryCutsCanCauseInternalBleedingWithoutMakingTinyTicksDangerous()
    {
        Assert.That(TraumaSystem.BleedingChance(0, 0, 8), Is.GreaterThan(0));
        Assert.That(TraumaSystem.BleedingChance(0, 0, 1), Is.Zero);
        Assert.That(TraumaSystem.BleedingChance(0, 2, 0), Is.Zero);
        Assert.That(TraumaSystem.BleedingChance(0, 0, 0), Is.Zero);
        Assert.That(TraumaSystem.BleedingChance(0, 0, 20), Is.GreaterThan(TraumaSystem.BleedingChance(20, 0, 0)));
    }

    [TestCase("CombatKnife")]
    [TestCase("N14WastelandTribalSpear")]
    [TestCase("N14SuperSledgeHammer")]
    public async Task WeaponDamageProducesReportedBleedingThatBandagingDoesNotStop(string weaponId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var weapon = em.SpawnEntity(weaponId, map.GridCoords);
            var trauma = em.System<TraumaSystem>();
            var medical = em.GetComponent<TraumaBodyComponent>(patient);
            var hit = em.GetComponent<MeleeWeaponComponent>(weapon).Damage;
            em.System<DamageableSystem>().TryChangeDamage(patient, hit,
                canSever: false, canEvade: false, targetPart: TargetBodyPart.Torso);
            // Fix the injury roll, not the injury itself: exercise post-armor damage events and the pending queue.
            var seed = Enumerable.Range(0, 100000).First(i => new System.Random(i).NextDouble() < .001);
            pair.Server.ResolveDependency<IRobustRandom>().SetSeed(seed);
            trauma.Update(.2f);
            Assert.That(medical.InternalBleeding, Is.GreaterThan(0), weaponId);
            Assert.That(trauma.GetReport(patient), Does.Contain("Internal bleeding:"));
            var rate = medical.InternalBleeding;

            // This is the same external bleed operation used by gauze's HealingSystem.
            em.System<BloodstreamSystem>().TryModifyBleedAmount(patient, -100);
            Assert.That(em.GetComponent<BloodstreamComponent>(patient).BleedAmount, Is.Zero);
            Assert.That(medical.InternalBleeding, Is.EqualTo(rate));
            Assert.That(trauma.GetReport(patient), Does.Contain("Internal bleeding:"));

            var torso = em.System<SharedBodySystem>().GetBodyChildrenOfType(patient, BodyPartType.Torso).First().Id;
            trauma.Repair(patient, torso, TraumaKind.InternalBleeding);
            Assert.That(medical.InternalBleeding, Is.Zero);
            Assert.That(trauma.GetReport(patient), Does.Not.Contain("Internal bleeding:"));
            em.DeleteEntity(patient);
            em.DeleteEntity(weapon);
        });
        await pair.CleanReturnAsync();
    }
}
