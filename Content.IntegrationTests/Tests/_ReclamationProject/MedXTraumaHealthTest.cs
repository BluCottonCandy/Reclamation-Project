using System.Linq;
using Content.Server.Administration;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Administration;
using Content.Shared._ReclamationProject.Endurance;
using Robust.Shared.Timing;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared._Shitmed.Targeting;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class MedXTraumaHealthTest
{
    [Test]
    public async Task TemporaryLimbFunctionAndSurgicalSalvageLimits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var _ent = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            void Check(bool ok, string label) => Assert.That(ok, Is.True, label);
            var trauma = _ent.System<TraumaSystem>(); var body = _ent.System<SharedBodySystem>();
            var solutions = _ent.System<SolutionContainerSystem>();
            var coords = map.GridCoords;
            var subject = _ent.SpawnEntity("MobHuman", coords); trauma.InitializeBody(subject);
            EntityUid Organ(TraumaKind kind) => body.GetBodyOrgans(subject).First(o => _ent.TryGetComponent<TraumaOrganComponent>(o.Id, out var c) && c.Kind == kind).Id;
            var heart = Organ(TraumaKind.Heart); var liver = Organ(TraumaKind.Liver); var kidneys = Organ(TraumaKind.Kidneys); var stomach = Organ(TraumaKind.Stomach);
            var h = _ent.GetComponent<TraumaOrganComponent>(heart); var l = _ent.GetComponent<TraumaOrganComponent>(liver); var k = _ent.GetComponent<TraumaOrganComponent>(kidneys); var st = _ent.GetComponent<TraumaOrganComponent>(stomach);
            trauma.SetOrganDamage(heart, 100); var heartMax = h.MaximumHealth;
            st.MaximumHealth = 0; st.FailureRecorded = true; trauma.SetOrganDamage(stomach, 0);
            var arm = body.GetBodyChildren(subject).First(p => p.Component.PartType == BodyPartType.Arm).Id;
            trauma.Fracture(arm); var limb = _ent.GetComponent<TraumaLimbComponent>(arm); var limbMax = limb.MaximumHealth;
            Check(!body.GetBodyOrgans(subject).First(o => o.Id == heart).Component.Enabled && !_ent.GetComponent<BodyPartComponent>(arm).Enabled, "test begins with disabled heart, destroyed stomach, and fractured arm");
            Check(solutions.TryGetSolution(subject, "chemicals", out var chemicals, out var mixture), "body chemicals solution exists");
            solutions.TryAddReagent(chemicals!.Value, "DamageResistMixture", 10, out _);
            Check(l.MaximumHealth == 85 && k.MaximumHealth == 85, "first dose permanently removes fifteen from each filter, with no filter boost");
            Check(h.CurrentHealth == 0 && !_ent.GetComponent<OrganComponent>(heart).Enabled, "Med-X does not revive a failed heart");
            Check(st.MaximumHealth == 0 && st.CurrentHealth == 0 && !_ent.GetComponent<OrganComponent>(stomach).Enabled, "destroyed organs receive no temporary function");
            Check(limb.TemporaryHealth == 15 && trauma.GetLimbHealth(arm, limb) > 14.8f && _ent.GetComponent<BodyPartComponent>(arm).Enabled, "Med-X restores use of a fractured disabled arm");
            Check(_ent.HasComponent<FractureComponent>(arm), "temporary function does not mend fracture");
            solutions.UpdateChemicals(chemicals.Value);
            Check(l.MaximumHealth == 85 && k.MaximumHealth == 85, "unchanged chemical updates do not charge another dose");
            solutions.TryAddReagent(chemicals.Value, "Dylovene", 1, out _);
            Check(l.MaximumHealth == 85 && k.MaximumHealth == 85, "other medicines do not charge Med-X wear");
            solutions.TryAddReagent(chemicals.Value, "DamageResistMixture", 5, out _);
            Check(l.MaximumHealth == 70 && k.MaximumHealth == 70 && limb.TemporaryHealth == 15, "overlapping second dose costs another fifteen without stacking temporary health");
            solutions.RemoveReagent(chemicals.Value, "DamageResistMixture", 1);
            Check(l.MaximumHealth == 70 && k.MaximumHealth == 70 && limb.TemporaryHealth == 15, "metabolism or partial removal does not charge wear or end boost early");
            var medical = _ent.GetComponent<TraumaBodyComponent>(subject); medical.SuppressInjuryRolls = true;
            _ent.System<DamageableSystem>().TryChangeDamage(arm, new DamageSpecifier { DamageDict = new() { ["Blunt"] = FixedPoint2.New(_ent.GetComponent<BodyPartComponent>(arm).IntegrityThresholds[TargetIntegrity.CriticallyWounded] + 2 - _ent.GetComponent<DamageableComponent>(arm).TotalDamage.Float()) } }, ignoreResistances: true, doPartDamage: false, canSever: false);
            medical.SuppressInjuryRolls = false;
            Check(trauma.GetLimbHealth(arm, limb) == 0 && _ent.GetComponent<BodyPartComponent>(arm).Enabled, "attached arm remains usable even after temporary health is exhausted");
            var disable = new Content.Shared._Shitmed.Body.Events.BodyPartEnableChangedEvent(false); _ent.EventBus.RaiseLocalEvent(arm, ref disable);
            Check(_ent.GetComponent<BodyPartComponent>(arm).Enabled, "Med-X blocks repeated disable events while active");
            solutions.RemoveReagent(chemicals.Value, "DamageResistMixture", 1000);
            Check(h.CurrentHealth == 0 && !_ent.GetComponent<OrganComponent>(heart).Enabled, "failed heart stays disabled through withdrawal");
            Check(!_ent.GetComponent<OrganComponent>(stomach).Enabled, "destroyed organ stays disabled through withdrawal");
            Check(limb.TemporaryHealth == 0 && !_ent.GetComponent<BodyPartComponent>(arm).Enabled && _ent.HasComponent<FractureComponent>(arm), "withdrawal restores fractured arm disability");
            Check(h.MaximumHealth == heartMax && limb.MaximumHealth == limbMax, "temporary recovery and withdrawal do not reroll existing permanent wear");
            var rage = _ent.EnsureComponent<EnduranceEnragedComponent>(subject); rage.Active = true;
            rage.EndsAt = pair.Server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(15);
            var surgery = _ent.System<TraumaSurgerySystem>(); surgery.Update(0);
            Check(_ent.GetComponent<BodyPartComponent>(arm).Enabled && surgery.HasLimbDisableProtection(arm), "Enraged restores a broken disabled arm without Med-X");
            var rageDisable = new Content.Shared._Shitmed.Body.Events.BodyPartEnableChangedEvent(false); _ent.EventBus.RaiseLocalEvent(arm, ref rageDisable);
            Check(_ent.GetComponent<BodyPartComponent>(arm).Enabled, "Enraged prevents further limb disabling");
            Check(h.CurrentHealth == 0 && !_ent.GetComponent<OrganComponent>(heart).Enabled, "Enraged limb protection does not revive failed organs");
            rage.Active = false; surgery.Update(0);
            Check(!_ent.GetComponent<BodyPartComponent>(arm).Enabled && !surgery.HasLimbDisableProtection(arm), "ending Enraged restores the broken arm disability");
            var boneSetting = new TraumaRepairEvent(arm, TraumaKind.Fracture); _ent.EventBus.RaiseLocalEvent(subject, ref boneSetting);
            Check(limb.MaximumHealth <= 15 && trauma.GetLimbHealth(arm, limb) == limb.MaximumHealth && _ent.GetComponent<BodyPartComponent>(arm).Enabled && !_ent.HasComponent<FractureComponent>(arm), "bone-setting surgery restores disabled arm only to fifteen-point permanent cap");
            trauma.Repair(subject, arm, TraumaKind.LimbDamage);
            Check(limb.MaximumHealth <= 15 && trauma.GetLimbHealth(arm, limb) <= 15, "full tissue surgery cannot bypass disabled-limb permanent cap");
            Check(!surgery.NeedsRepair(subject, arm, TraumaKind.Fracture), "repeating bone setting cannot increase remaining capacity");
            var foot = body.GetBodyChildren(subject).First(p => p.Component.PartType == BodyPartType.Foot).Id;
            trauma.Fracture(foot); _ent.RemoveComponent<FractureComponent>(foot);
            Check(surgery.NeedsRepair(subject, foot, TraumaKind.Fracture), "bone setting is available for fully disabled tissue without a fracture marker");
            var tissueSetting = new TraumaRepairEvent(foot, TraumaKind.Fracture); _ent.EventBus.RaiseLocalEvent(subject, ref tissueSetting);
            Check(_ent.GetComponent<BodyPartComponent>(foot).Enabled && trauma.GetLimbHealth(foot, _ent.GetComponent<TraumaLimbComponent>(foot)) <= 15, "bone setter restores function to fully disabled nonfractured limb");
            var zeroLeg = body.GetBodyChildren(subject).First(p => p.Component.PartType == BodyPartType.Leg && p.Component.Symmetry == _ent.GetComponent<BodyPartComponent>(foot).Symmetry).Id;
            var zeroHealth = _ent.GetComponent<TraumaLimbComponent>(zeroLeg); var zeroPart = _ent.GetComponent<BodyPartComponent>(zeroLeg);
            var zeroRatio = .00001f / Math.Max(.00001f, zeroHealth.AppliedMaximumHealth);
            foreach (var key in zeroPart.IntegrityThresholds.Keys.ToArray()) zeroPart.IntegrityThresholds[key] *= zeroRatio;
            zeroHealth.MaximumHealth = 0; zeroHealth.AppliedMaximumHealth = 0; zeroHealth.FailureRecorded = true;
            _ent.System<DamageableSystem>().SetAllDamage(zeroLeg, _ent.GetComponent<DamageableComponent>(zeroLeg), FixedPoint2.Zero);
            Check(surgery.NeedsRepair(subject, zeroLeg, TraumaKind.Fracture), "bone-setting procedure is offered for a zero-capacity 0/0 leg");
            var zeroSetting = new TraumaRepairEvent(zeroLeg, TraumaKind.Fracture); _ent.EventBus.RaiseLocalEvent(subject, ref zeroSetting);
            Check(zeroHealth.MaximumHealth == 15 && trauma.GetLimbHealth(zeroLeg, zeroHealth) == 15 && zeroPart.Enabled, "bone setter salvages 0/0 leg to functional 15/15");
            trauma.Repair(subject, zeroLeg, TraumaKind.LimbDamage);
            Check(zeroHealth.MaximumHealth == 15 && trauma.GetLimbHealth(zeroLeg, zeroHealth) == 15, "other surgery cannot raise salvaged leg above fifteen");
            Check(!surgery.NeedsRepair(subject, zeroLeg, TraumaKind.Fracture), "bone-setting cannot be repeated to stack a salvaged leg's capacity");
            var allBones = body.GetBodyChildren(subject).Where(p => p.Component.PartType is BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot).ToArray();
            foreach (var (part, component) in allBones)
            {
                _ent.RemoveComponent<FractureComponent>(part);
                var health = _ent.GetComponent<TraumaLimbComponent>(part); var ratio = .00001f / Math.Max(.00001f, health.AppliedMaximumHealth);
                foreach (var key in component.IntegrityThresholds.Keys.ToArray()) component.IntegrityThresholds[key] *= ratio;
                health.MaximumHealth = 0; health.AppliedMaximumHealth = 0; health.FailureRecorded = true;
                _ent.System<DamageableSystem>().SetAllDamage(part, _ent.GetComponent<DamageableComponent>(part), FixedPoint2.Zero);
                var disabledBone = new Content.Shared._Shitmed.Body.Events.BodyPartEnableChangedEvent(false); _ent.EventBus.RaiseLocalEvent(part, ref disabledBone);
            }
            foreach (var (part, component) in allBones)
            {
                Check(surgery.NeedsRepair(subject, part, TraumaKind.Fracture), $"bone setting offered for 0/0 {component.Symmetry} {component.PartType}");
                var salvageBone = new TraumaRepairEvent(part, TraumaKind.Fracture); _ent.EventBus.RaiseLocalEvent(subject, ref salvageBone);
                var health = _ent.GetComponent<TraumaLimbComponent>(part);
                Check(health.MaximumHealth == 15 && trauma.GetLimbHealth(part, health) == 15 && component.Enabled, $"bone setter restores {component.Symmetry} {component.PartType} to 15/15");
            }
            solutions.TryAddReagent(chemicals.Value, "Hydra", 5, out _);
            Check(l.MaximumHealth == 70, "Hydra does not receive Med-X temporary health or filter costs");
            var blocked = _ent.SpawnEntity("MobHuman", coords); trauma.InitializeBody(blocked);
            var blockedLiver = body.GetBodyOrgans(blocked).First(o => _ent.TryGetComponent<TraumaOrganComponent>(o.Id, out var c) && c.Kind == TraumaKind.Liver).Id;
            var blockedArm = body.GetBodyChildren(blocked).First(p => p.Component.PartType == BodyPartType.Arm).Id;
            trauma.SetOrganDamage(blockedLiver, 100);
            solutions.TryGetSolution(blocked, "chemicals", out var blockedSolution, out _);
            solutions.TryAddReagent(blockedSolution!.Value, "DamageResistMixture", 10, out _);
            Check(_ent.GetComponent<TraumaLimbComponent>(blockedArm).TemporaryHealth == 0, "failed filter prevents Med-X function like other chemical effects");
            _ent.QueueDeleteEntity(blocked);
            _ent.QueueDeleteEntity(subject);
        });
        await pair.CleanReturnAsync();
    }
}
