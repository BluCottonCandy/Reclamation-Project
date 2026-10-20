using System.Linq;
using Content.Server.EntityEffects.Effects;
using Content.Shared.EntityEffects;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Robust.Shared.Prototypes;
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
    [TestCase(false, true, false, 1f)]
    [TestCase(true, true, false, 3f)]
    [TestCase(false, false, true, 1f)]
    [TestCase(false, true, true, 5f)]
    [TestCase(true, true, true, 5f)]
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
    [TestCase("HealingMixture")]
    [TestCase("DirtyStimpak")]
    [TestCase("RobustHealingMixture")]
    [TestCase("UltraRobustHealingMixture")]
    [TestCase("Omnizine")]
    public async Task StimpackAndMedicineHealingCannotRepairStructuralInjuries(string reagentId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var trauma = em.System<TraumaSystem>();
            var body = em.System<SharedBodySystem>();
            var damage = em.System<DamageableSystem>();
            var arm = body.GetBodyChildrenOfType(patient, BodyPartType.Arm).First().Id;
            var partDamage = em.GetComponent<DamageableComponent>(arm);
            damage.SetAllDamage(arm, partDamage, 2);
            damage.TryChangeDamage(patient, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 40 } }, doPartDamage: false);
            trauma.Fracture(arm);
            trauma.AddInternalBleeding(patient, 2);
            var lung = body.GetBodyOrgans(patient).First(o => em.TryGetComponent<TraumaOrganComponent>(o.Id, out var c) && c.Kind == TraumaKind.Lungs).Id;
            trauma.SetOrganDamage(lung, 50);
            var medical = em.GetComponent<TraumaBodyComponent>(patient);
            var beforeLimb = partDamage.Damage.DamageDict["Blunt"];
            var beforeBody = em.GetComponent<DamageableComponent>(patient).Damage.DamageDict["Blunt"];
            var reagent = pair.Server.ResolveDependency<IPrototypeManager>().Index<ReagentPrototype>(reagentId);
            var effects = reagent.Metabolisms!.Values.SelectMany(m => m.Effects).OfType<HealthChange>().ToArray();
            Assert.That(effects, Is.Not.Empty);
            foreach (var effect in effects)
            {
                var original = new DamageSpecifier(effect.Damage);
                effect.Effect(new EntityEffectBaseArgs(patient, em));
                Assert.That(effect.Damage.DamageDict, Is.EquivalentTo(original.DamageDict), "Healing must not mutate cached medicine effects.");
            }
            Assert.That(partDamage.Damage.DamageDict["Blunt"], Is.EqualTo(beforeLimb));
            Assert.That(em.GetComponent<DamageableComponent>(patient).Damage.DamageDict["Blunt"], Is.LessThan(beforeBody), "Ordinary health damage must remain healable.");
            Assert.That(em.GetComponent<TraumaOrganComponent>(lung).Damage, Is.EqualTo(50));
            Assert.That(medical.InternalBleeding, Is.EqualTo(2));
            Assert.That(em.GetComponent<FractureComponent>(arm).HealingProgress, Is.Zero);
            trauma.Repair(patient, arm, TraumaKind.LimbDamage);
            Assert.That(partDamage.TotalDamage, Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.Zero));
            em.DeleteEntity(patient);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MixedDamageKeepsHarmWhileRejectingStructuralHealing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var arm = em.System<SharedBodySystem>().GetBodyChildrenOfType(patient, BodyPartType.Arm).First().Id;
            var damage = em.GetComponent<DamageableComponent>(arm);
            em.System<DamageableSystem>().SetAllDamage(arm, damage, 2);
            var healing = new DamageSpecifier { DamageDict = new() { ["Blunt"] = -50, ["Heat"] = 5 } };
            em.System<DamageableSystem>().TryChangeDamage(arm, healing, ignoreResistances: true, canSever: false);
            Assert.That(damage.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(2));
            Assert.That(damage.Damage.DamageDict["Heat"].Float(), Is.EqualTo(7));
            Assert.That(healing.DamageDict["Blunt"].Float(), Is.EqualTo(-50));
            var before = damage.TotalDamage;
            em.System<TraumaSystem>().HealNaturally(patient, em.GetComponent<TraumaBodyComponent>(patient), 60);
            Assert.That(damage.TotalDamage, Is.LessThan(before));
            em.DeleteEntity(patient);
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task ChemicalsNeitherHealNorDamageAttachedOrRemovedPlayerParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var body = em.System<SharedBodySystem>();
            var arm = body.GetBodyChildrenOfType(patient, BodyPartType.Arm).First().Id;
            var organ = body.GetBodyOrgans(patient).First(o => em.HasComponent<TraumaOrganComponent>(o.Id)).Id;
            var damage = em.GetComponent<DamageableComponent>(arm);
            em.System<DamageableSystem>().SetAllDamage(arm, damage, 2);
            em.System<TraumaSystem>().SetOrganDamage(organ, 50);
            var before = damage.TotalDamage;
            foreach (var delta in new[] { -50, 50 })
            {
                var effect = new HealthChange { Damage = new DamageSpecifier { DamageDict = new() { ["Blunt"] = delta } } };
                effect.Effect(new EntityEffectBaseArgs(patient, em));
                effect.Effect(new EntityEffectBaseArgs(arm, em));
                effect.Effect(new EntityEffectBaseArgs(organ, em));
                Assert.That(damage.TotalDamage, Is.EqualTo(before));
                Assert.That(em.GetComponent<TraumaOrganComponent>(organ).Damage, Is.EqualTo(50));
            }
            var amputate = new AmputateAttemptEvent(arm);
            em.EventBus.RaiseLocalEvent(arm, ref amputate);
            Assert.That(em.GetComponent<BodyPartComponent>(arm).Body, Is.Null);
            Assert.That(body.RemoveOrgan(organ), Is.True);
            foreach (var target in new[] { arm, organ })
            {
                var effect = new HealthChange { Damage = new DamageSpecifier { DamageDict = new() { ["Blunt"] = -50 } } };
                effect.Effect(new EntityEffectBaseArgs(target, em));
            }
            em.System<DamageableSystem>().TryChangeDamage(arm, new DamageSpecifier { DamageDict = new() { ["Blunt"] = -50 } }, ignoreResistances: true);
            Assert.That(damage.TotalDamage, Is.EqualTo(before));
            Assert.That(em.GetComponent<TraumaOrganComponent>(organ).Damage, Is.EqualTo(50));
            em.DeleteEntity(patient);
            em.DeleteEntity(arm);
            em.DeleteEntity(organ);
        });
        await pair.CleanReturnAsync();
    }
}
