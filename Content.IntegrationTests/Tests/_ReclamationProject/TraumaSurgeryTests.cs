using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._ReclamationProject.Medical;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class TraumaSurgeryTests
{
    [TestCase(TraumaKind.LimbDamage, BodyPartType.Arm)]
    [TestCase(TraumaKind.LimbDamage, BodyPartType.Head)]
    [TestCase(TraumaKind.Fracture, BodyPartType.Arm)]
    [TestCase(TraumaKind.InternalBleeding, BodyPartType.Torso)]
    [TestCase(TraumaKind.Lungs, BodyPartType.Torso)]
    [TestCase(TraumaKind.Heart, BodyPartType.Torso)]
    [TestCase(TraumaKind.Liver, BodyPartType.Torso)]
    [TestCase(TraumaKind.Kidneys, BodyPartType.Torso)]
    [TestCase(TraumaKind.Eyes, BodyPartType.Head)]
    [TestCase(TraumaKind.Stomach, BodyPartType.Torso)]
    public async Task InjuryCanBeRepairedThroughRealToolDoAfters(TraumaKind kind, BodyPartType partType)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid patient = default, doctor = default, part = default;
        var tools = new List<EntityUid>();
        var origin = map.GridCoords.Offset(new Vector2(.5f, .5f));
        var toolIds = new Dictionary<string, string>
        {
            ["Scalpel"] = "Scalpel", ["Retractor"] = "Retractor", ["Hemostat"] = "Hemostat",
            ["Cautery"] = "Cautery", ["BoneSetter"] = "Bonesetter", ["BoneGel"] = "BoneGel", ["BoneSaw"] = "Saw",
        };
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                em.System<SharedMapSystem>().SetTile(map.Grid, origin.Offset(new Vector2(x, y)), map.Tile.Tile);
            patient = em.SpawnTraumaPatient(origin);
            doctor = em.SpawnTraumaPatient(origin.Offset(new Vector2(1.3f, 0)));
            var body = em.System<SharedBodySystem>();
            part = body.GetBodyChildrenOfType(patient, partType).First().Id;
            var trauma = em.System<TraumaSystem>();
            if (kind == TraumaKind.LimbDamage)
            {
                var partName = em.GetComponent<MetaDataComponent>(part).EntityName;
                Assert.That(trauma.GetReport(patient), Does.Not.Contain(partName),
                    "Healthy parts should not clutter the injury scan.");
                em.System<DamageableSystem>().SetAllDamage(part, em.GetComponent<DamageableComponent>(part), 3);
                Assert.That(em.GetComponent<BodyPartComponent>(part).Enabled, Is.True,
                    "Early tissue repair must be available before limb failure.");
                if (partType == BodyPartType.Arm)
                    Assert.That(trauma.GetReport(patient), Does.Contain(partName));
            }
            else if (kind == TraumaKind.Fracture) trauma.Fracture(part);
            else if (kind == TraumaKind.InternalBleeding) trauma.AddInternalBleeding(patient, .5f);
            else
                foreach (var (organ, _) in body.GetPartOrgans(part))
                    if (em.TryGetComponent<TraumaOrganComponent>(organ, out var injury) && injury.Kind == kind)
                        trauma.SetOrganDamage(organ, 50);
            em.System<StandingStateSystem>().Down(patient, playSound: false, dropHeldItems: false);
            Assert.That(em.System<TraumaSurgerySystem>().NeedsRepair(patient, part, kind), Is.True);
        });
        var steps = 0;
        var complete = false;
        while (!complete && steps++ < 12)
        {
            EntityUid step = default;
            EntProtoId surgeryId = default;
            EntProtoId stepId = default;
            await pair.Server.WaitAssertion(() =>
            {
                var surgery = em.System<SharedSurgerySystem>();
                var procedure = surgery.GetSingleton("ReclamationSurgeryRepair" + kind)!.Value;
                var next = surgery.GetNextStep(patient, part, procedure);
                Assert.That(next, Is.Not.Null);
                surgeryId = em.GetComponent<MetaDataComponent>(next!.Value.Surgery).EntityPrototype!.ID;
                stepId = next.Value.Surgery.Comp.Steps[next.Value.Step];
                step = surgery.GetSingleton(stepId)!.Value;
                var component = em.GetComponent<SurgeryStepComponent>(step);
                var hands = em.System<SharedHandsSystem>();
                foreach (var held in hands.EnumerateHeld(doctor).ToArray()) hands.TryDrop(doctor, held);
                if (stepId == "ReclamationSurgeryStepRepairFracture")
                {
                    var mender = em.SpawnEntity("N14Wrench", origin);
                    tools.Add(mender);
                    Assert.That(hands.TryPickupAnyHand(doctor, mender), Is.True);
                }
                else if (component.Tool != null)
                    foreach (var registry in component.Tool.Values)
                    {
                        var typeName = registry.Component.GetType().Name.Replace("Component", "");
                        var tool = em.SpawnEntity(toolIds[typeName], origin);
                        tools.Add(tool);
                        Assert.That(hands.TryPickupAnyHand(doctor, tool), Is.True);
                    }
                Assert.That(hands.EnumerateHeld(doctor).Count(), Is.EqualTo(1),
                    "Every surgical step must be usable with one tool in one hand.");
                Assert.That(surgery.TryDoSurgeryStep(patient, part, doctor, surgeryId, stepId), Is.True,
                    $"Cannot start {surgeryId}/{stepId} with its required tools.");
            });
            await pair.RunSeconds(14);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(em.System<SharedSurgerySystem>().IsStepComplete(patient, part, stepId,
                    em.System<SharedSurgerySystem>().GetSingleton(surgeryId)!.Value), Is.True, $"Step {stepId} did not finish.");
                complete = !em.System<TraumaSurgerySystem>().NeedsRepair(patient, part, kind);
            });
        }
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(complete, Is.True);
            em.DeleteEntity(patient);
            em.DeleteEntity(doctor);
            foreach (var tool in tools) if (em.EntityExists(tool)) em.DeleteEntity(tool);
        });
        await pair.CleanReturnAsync();
    }
}
