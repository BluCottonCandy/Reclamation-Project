using System.Linq;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class TraumaMedicalTests
{
    [TestCase(TraumaKind.Lungs)]
    [TestCase(TraumaKind.Heart)]
    [TestCase(TraumaKind.Liver)]
    [TestCase(TraumaKind.Kidneys)]
    [TestCase(TraumaKind.Eyes)]
    [TestCase(TraumaKind.Stomach)]
    public async Task OrganInjuryChangesFunctionAndSurgeryRestoresIt(TraumaKind kind)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(em.HasComponent<TraumaBodyComponent>(patient), Is.True, "Organic humanoids must opt in automatically.");
            var trauma = em.System<TraumaSystem>();
            var body = em.System<SharedBodySystem>();
            var organ = body.GetBodyOrgans(patient).First(o => em.TryGetComponent<TraumaOrganComponent>(o.Id, out var injury) && injury.Kind == kind).Id;
            var part = body.GetBodyChildren(patient).First(p => body.GetPartOrgans(p.Id).Any(o => o.Id == organ)).Id;
            var stamina = em.GetComponent<StaminaComponent>(patient);
            var capacity = stamina.CritThreshold;
            var decay = stamina.Decay;
            var critical = em.System<MobThresholdSystem>().GetThresholdForState(patient, MobState.Critical);
            var interval = em.TryGetComponent<MetabolizerComponent>(organ, out var metabolizer) ? metabolizer.UpdateInterval : TimeSpan.Zero;
            trauma.SetOrganDamage(organ, 50);
            trauma.Refresh(patient, em.GetComponent<TraumaBodyComponent>(patient));
            switch (kind)
            {
                case TraumaKind.Lungs: Assert.That(stamina.CritThreshold, Is.EqualTo(capacity * .5f).Within(.001f)); break;
                case TraumaKind.Heart:
                    Assert.That(em.System<MobThresholdSystem>().GetThresholdForState(patient, MobState.Critical), Is.EqualTo(critical * .75f)); break;
                case TraumaKind.Liver:
                    var poison = new DamageModifyEvent(new DamageSpecifier { DamageDict = new() { ["Poison"] = 10 } });
                    em.EventBus.RaiseLocalEvent(patient, poison);
                    Assert.That(poison.Damage.DamageDict["Poison"], Is.EqualTo(FixedPoint2.New(15)));
                    Assert.That(metabolizer!.UpdateInterval, Is.GreaterThan(interval)); break;
                case TraumaKind.Kidneys:
                    Assert.That(stamina.Decay, Is.EqualTo(decay * .75f).Within(.001f));
                    Assert.That(metabolizer!.UpdateInterval, Is.GreaterThan(interval)); break;
                case TraumaKind.Eyes:
                    Assert.That(em.HasComponent<BlurryVisionComponent>(patient), Is.True);
                    trauma.SetOrganDamage(organ, 100);
                    Assert.That(em.GetComponent<BlindableComponent>(patient).IsBlind, Is.True); break;
                case TraumaKind.Stomach:
                    var hunger = em.System<HungerSystem>();
                    hunger.SetHunger(patient, 100);
                    hunger.ModifyHunger(patient, 10);
                    Assert.That(em.GetComponent<HungerComponent>(patient).CurrentHunger, Is.EqualTo(106).Within(.001)); break;
            }
            // A stimpak-like heal must not remove structural organ damage.
            em.System<DamageableSystem>().TryChangeDamage(patient, new DamageSpecifier { DamageDict = new() { ["Blunt"] = -100 } });
            Assert.That(em.GetComponent<TraumaOrganComponent>(organ).Damage, Is.GreaterThan(0));
            Assert.That(em.System<TraumaSurgerySystem>().NeedsRepair(patient, part, kind), Is.True);
            var repair = new TraumaRepairEvent(part, kind);
            em.EventBus.RaiseLocalEvent(patient, ref repair);
            Assert.That(em.GetComponent<TraumaOrganComponent>(organ).Damage, Is.Zero);
            Assert.That(stamina.CritThreshold, Is.EqualTo(capacity).Within(.001f));
            Assert.That(stamina.Decay, Is.EqualTo(decay).Within(.001f));
            Assert.That(em.System<MobThresholdSystem>().GetThresholdForState(patient, MobState.Critical), Is.EqualTo(critical));
            if (metabolizer != null) Assert.That(metabolizer.UpdateInterval.TotalSeconds, Is.EqualTo(interval.TotalSeconds).Within(.00001));
            if (kind == TraumaKind.Eyes) Assert.That(em.GetComponent<BlindableComponent>(patient).IsBlind, Is.False);
            Assert.That(em.System<TraumaSurgerySystem>().NeedsRepair(patient, part, kind), Is.False);
            em.DeleteEntity(patient);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(BodyPartType.Arm)]
    [TestCase(BodyPartType.Hand)]
    [TestCase(BodyPartType.Leg)]
    [TestCase(BodyPartType.Foot)]
    public async Task FracturesStayDisabledThroughHealingAndRepairRestoresUse(BodyPartType type)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnEntity("MobHuman", map.GridCoords);
            var body = em.System<SharedBodySystem>();
            var (part, component) = body.GetBodyChildrenOfType(patient, type).First();
            em.System<TraumaSystem>().Fracture(part);
            Assert.That(component.Enabled, Is.False);
            var enable = new BodyPartEnableChangedEvent(true);
            em.EventBus.RaiseLocalEvent(part, ref enable);
            Assert.That(component.Enabled, Is.False, "Passive healing must not enable a broken limb.");
            em.System<DamageableSystem>().TryChangeDamage(part, new DamageSpecifier { DamageDict = new() { ["Blunt"] = -100 } });
            Assert.That(component.Enabled, Is.False);
            em.System<TraumaSystem>().Repair(patient, part, TraumaKind.Fracture);
            Assert.That(em.HasComponent<FractureComponent>(part), Is.False);
            Assert.That(component.Enabled, Is.True);
            em.DeleteEntity(patient);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InternalBleedingPersistsThroughHealingAndSurgeryStopsIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid patient = default;
        float before = 0;
        await pair.Server.WaitAssertion(() =>
        {
            patient = em.SpawnEntity("MobHuman", map.GridCoords);
            var bloodstream = em.GetComponent<BloodstreamComponent>(patient);
            Assert.That(em.System<SharedSolutionContainerSystem>().ResolveSolution(patient, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var blood), Is.True);
            before = blood!.Volume.Float();
            em.System<TraumaSystem>().AddInternalBleeding(patient, 2);
            em.System<BloodstreamSystem>().TryModifyBleedAmount(patient, -100);
            em.System<DamageableSystem>().TryChangeDamage(patient, new DamageSpecifier { DamageDict = new() { ["Blunt"] = -100 } });
            Assert.That(em.GetComponent<TraumaBodyComponent>(patient).InternalBleeding, Is.EqualTo(2));
        });
        await pair.RunSeconds(3);
        await pair.Server.WaitAssertion(() =>
        {
            var bloodstream = em.GetComponent<BloodstreamComponent>(patient);
            Assert.That(em.System<SharedSolutionContainerSystem>().ResolveSolution(patient, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var blood), Is.True);
            Assert.That(blood!.Volume.Float(), Is.LessThan(before));
            var torso = em.System<SharedBodySystem>().GetBodyChildrenOfType(patient, BodyPartType.Torso).First().Id;
            em.System<TraumaSystem>().Repair(patient, torso, TraumaKind.InternalBleeding);
            Assert.That(em.GetComponent<TraumaBodyComponent>(patient).InternalBleeding, Is.Zero);
            em.DeleteEntity(patient);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BrainRejectsDamageAndDirectDamageAssignment()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var brain = em.SpawnEntity("OrganHumanBrain", map.GridCoords);
            var damageable = em.EnsureComponent<DamageableComponent>(brain);
            var damage = em.System<DamageableSystem>();
            Assert.That(damage.TryChangeDamage(brain, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 100 } }), Is.Null);
            damage.SetAllDamage(brain, damageable, 10);
            Assert.That(damageable.TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(em.HasComponent<TraumaOrganComponent>(brain), Is.False);
            em.DeleteEntity(brain);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FailedLungsCauseSuffocation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid patient = default;
        await pair.Server.WaitAssertion(() =>
        {
            patient = em.SpawnEntity("MobHuman", map.GridCoords);
            var lungs = em.System<SharedBodySystem>().GetBodyOrgans(patient)
                .First(o => em.TryGetComponent<TraumaOrganComponent>(o.Id, out var injury) && injury.Kind == TraumaKind.Lungs).Id;
            em.System<TraumaSystem>().SetOrganDamage(lungs, 100);
            Assert.That(em.GetComponent<StaminaComponent>(patient).CritThreshold, Is.LessThan(.1f));
        });
        await pair.RunSeconds(14);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<DamageableComponent>(patient).Damage.DamageDict["Asphyxiation"].Float(), Is.GreaterThan(0));
            em.DeleteEntity(patient);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransplantsPreserveInjuriesAndMissingLungsFail()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var donor = em.SpawnEntity("MobHuman", map.GridCoords);
            var recipient = em.SpawnEntity("MobHuman", map.GridCoords);
            var body = em.System<SharedBodySystem>();
            var trauma = em.System<TraumaSystem>();
            var organ = body.GetBodyOrgans(donor).First(o => em.TryGetComponent<TraumaOrganComponent>(o.Id, out var injury) && injury.Kind == TraumaKind.Lungs).Id;
            var recipientLung = body.GetBodyOrgans(recipient).First(o => em.TryGetComponent<TraumaOrganComponent>(o.Id, out var injury) && injury.Kind == TraumaKind.Lungs).Id;
            var torso = body.GetBodyChildrenOfType(recipient, BodyPartType.Torso).First().Id;
            trauma.SetOrganDamage(organ, 50);
            Assert.That(body.RemoveOrgan(organ), Is.True);
            Assert.That(body.RemoveOrgan(recipientLung), Is.True);
            Assert.That(body.InsertOrgan(torso, organ, "lungs"), Is.True);
            trauma.Refresh(donor, em.GetComponent<TraumaBodyComponent>(donor));
            trauma.Refresh(recipient, em.GetComponent<TraumaBodyComponent>(recipient));
            Assert.That(em.GetComponent<TraumaBodyComponent>(donor).LungDamage, Is.EqualTo(100));
            Assert.That(em.GetComponent<TraumaBodyComponent>(recipient).LungDamage, Is.EqualTo(50));
            em.DeleteEntity(donor); em.DeleteEntity(recipient); em.DeleteEntity(recipientLung);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RepairingAnArmDoesNotBypassItsBrokenHand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnEntity("MobHuman", map.GridCoords);
            var item = em.SpawnEntity("Scalpel", map.GridCoords);
            var body = em.System<SharedBodySystem>();
            var trauma = em.System<TraumaSystem>();
            foreach (var (limb, _) in body.GetBodyChildrenOfType(patient, BodyPartType.Arm)
                         .Concat(body.GetBodyChildrenOfType(patient, BodyPartType.Hand))) trauma.Fracture(limb);
            var arm = body.GetBodyChildrenOfType(patient, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left).First().Id;
            var hand = body.GetBodyChildrenOfType(patient, BodyPartType.Hand, symmetry: BodyPartSymmetry.Left).First().Id;
            trauma.Repair(patient, arm, TraumaKind.Fracture);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(patient, item), Is.False);
            trauma.Repair(patient, hand, TraumaKind.Fracture);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(patient, item), Is.True);
            em.DeleteEntity(patient);
            if (em.EntityExists(item)) em.DeleteEntity(item);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public void DamageTypesFavorTheIntendedInjuries()
    {
        Assert.That(TraumaSystem.FractureChance(30, 0, 0), Is.GreaterThan(TraumaSystem.FractureChance(0, 30, 0)));
        Assert.That(TraumaSystem.OrganChance(30, 0, 0), Is.GreaterThan(TraumaSystem.OrganChance(0, 30, 0)));
        Assert.That(TraumaSystem.BleedingChance(0, 30, 0), Is.GreaterThan(TraumaSystem.BleedingChance(30, 0, 0)));
        Assert.That(TraumaSystem.BleedingChance(0, 0, 30), Is.GreaterThan(TraumaSystem.BleedingChance(30, 0, 0)));
        Assert.That(TraumaSystem.FractureChance(2, 0, 0), Is.Zero);
        Assert.That(TraumaSystem.BleedingChance(0, 2, 0), Is.Zero);
        foreach (var (blunt, piercing, slash, other) in new[] { (30f, 0f, 0f, 0f), (0f, 30f, 0f, 0f),
                     (0f, 0f, 30f, 0f), (0f, 0f, 0f, 30f) })
        {
            Assert.That(TraumaSystem.FractureChance(blunt, piercing, slash, other), Is.GreaterThan(0));
            Assert.That(TraumaSystem.BleedingChance(blunt, piercing, slash, other), Is.GreaterThan(0));
            Assert.That(TraumaSystem.OrganChance(blunt, piercing, slash, other), Is.GreaterThan(0));
        }
        Assert.That(TraumaSystem.FractureChance(0, 0, 30), Is.LessThanOrEqualTo(.05f));
        Assert.That(TraumaSystem.BleedingChance(30, 0, 0), Is.LessThanOrEqualTo(.05f));
        Assert.That(TraumaSystem.OrganChance(0, 0, 0, 300), Is.LessThanOrEqualTo(.05f));
    }
}
