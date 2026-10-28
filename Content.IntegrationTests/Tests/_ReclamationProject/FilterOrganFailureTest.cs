using Robust.Shared.GameObjects;
using System.Linq;
using System.Reflection;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;


namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class FilterOrganFailureTest
{
    [Test]
    public async Task FilterFailurePoisonsAndStopsChemicalProcessing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            void Check(bool ok, string name) => Assert.That(ok, Is.True, name);
            var subject = entities.SpawnEntity("MobHuman", map.GridCoords);
            var trauma = entities.System<TraumaSystem>(); var body = entities.System<SharedBodySystem>(); trauma.InitializeBody(subject);
            var torso = body.GetBodyChildren(subject).First(o => o.Component.PartType == BodyPartType.Torso).Id;
            EntityUid Organ(TraumaKind kind) => body.GetBodyOrgans(subject).First(o => entities.TryGetComponent<TraumaOrganComponent>(o.Id, out var h) && h.Kind == kind).Id;
            var liver = Organ(TraumaKind.Liver); var kidneys = Organ(TraumaKind.Kidneys);
            var damage = entities.GetComponent<DamageableComponent>(subject);
            float Poison() => damage.Damage.DamageDict.GetValueOrDefault("Poison").Float();
            Check(trauma.GetFailedFilterCount(subject) == 0, "healthy filter organs do not poison");
            trauma.SetOrganDamage(liver, 100);
            Check(trauma.GetFailedFilterCount(subject) == 1, "failed liver counts once");
            var before = Poison(); trauma.Update(1);
            Check(Poison() - before == 1, "one failed filter deals exactly one poison per second without liver doubling");
            Check(!entities.GetComponent<OrganComponent>(liver).Enabled, "tiny natural healing does not bypass failure");
            var attempt = new MedicalTreatmentAttemptEvent(IsMetabolism: true); entities.EventBus.RaiseLocalEvent(subject, ref attempt);
            Check(attempt.Cancelled, "liver failure blocks metabolism");
            attempt = new MedicalTreatmentAttemptEvent(); entities.EventBus.RaiseLocalEvent(subject, ref attempt);
            Check(!attempt.Cancelled, "physical treatment remains allowed");
            trauma.SetOrganDamage(kidneys, 100); before = Poison(); trauma.Update(1);
            Check(trauma.GetFailedFilterCount(subject) == 2 && Poison() - before == 2, "two failed filters deal exactly two poison per second");
            var solutions = entities.System<SolutionContainerSystem>();
            Check(solutions.TryGetSolution(subject, entities.GetComponent<BloodstreamComponent>(subject).ChemicalSolutionName, out var chemicalEntity, out var chemicals), "bloodstream chemical reservoir available");
            Check(solutions.TryAddReagent(chemicalEntity!.Value, "Omnizine", FixedPoint2.New(5)), "test chemical added");
            var volume = chemicals!.Volume;
            entities.System<DamageableSystem>().TryChangeDamage(subject, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 10 } }, ignoreResistances: true, doPartDamage: false);
            var blunt = damage.Damage.DamageDict["Blunt"];
            var method = typeof(MetabolizerSystem).GetMethod("TryMetabolize", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void ProcessChemicals()
            {
                foreach (var pair in body.GetBodyOrganComponents<MetabolizerComponent>(subject))
                {
                    Entity<MetabolizerComponent, OrganComponent?, SolutionContainerManagerComponent?> entity = (pair.Comp.Owner, pair.Comp, pair.Organ, null);
                    method.Invoke(entities.System<MetabolizerSystem>(), new object[] { entity });
                }
            }
            ProcessChemicals();
            Check(chemicals.Volume == volume && damage.Damage.DamageDict["Blunt"] == blunt, "failed filters preserve chemicals and prevent their healing effects");
            trauma.Repair(subject, torso, TraumaKind.Liver);
            Check(trauma.GetFailedFilterCount(subject) == 1, "repairing liver alone leaves kidney failure active");
            before = Poison(); trauma.Update(1); Check(Poison() - before == 1, "failed kidneys alone deal one poison per second");
            ProcessChemicals(); Check(chemicals.Volume == volume, "kidney failure alone still blocks all chemical metabolism");
            trauma.Repair(subject, torso, TraumaKind.Kidneys);
            Check(trauma.GetFailedFilterCount(subject) == 0, "repair restores both filter functions");
            before = Poison(); trauma.Update(1); Check(Poison() == before, "poison generation stops after repair");
            ProcessChemicals(); Check(chemicals.Volume < volume && damage.Damage.DamageDict["Blunt"] < blunt, "chemical consumption and effects resume after repair");
            Check(body.RemoveOrgan(liver) && trauma.GetFailedFilterCount(subject) == 1, "missing liver also prevents filtering");
            Check(body.InsertOrgan(torso, liver, "liver"), "repaired liver reinserted");
            Check(trauma.GetFailedFilterCount(subject) == 0, "reinsertion restores filtering");
            Check(trauma.GetReport(subject).Contains("HP"), "compact scanner health report retained");
            entities.QueueDeleteEntity(subject);

        });
        await pair.CleanReturnAsync();
    }
}
