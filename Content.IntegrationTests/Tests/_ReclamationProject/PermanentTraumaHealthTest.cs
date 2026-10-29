using Robust.Shared.GameObjects;
using System.Linq;
using Content.Server._ReclamationProject.Medical;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Serialization.Manager;


namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class PermanentTraumaHealthTest
{
    [Test]
    public async Task FailureWearRepairsTransplantsAndScanner()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var serialization = pair.Server.ResolveDependency<ISerializationManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var coords = map.GridCoords;
            void Check(bool ok, string name) => Assert.That(ok, Is.True, name);
            var trauma = entities.System<TraumaSystem>();
            var body = entities.System<SharedBodySystem>();
            EntityUid Spawn()
            {
                var u = entities.SpawnEntity("MobHuman", coords);
                trauma.InitializeBody(u);
                return u;
            }
            var subject = Spawn();
            var recipient = Spawn();
            Check(trauma.GetReport(subject).Contains("No fractures or internal injuries detected.") &&
                !trauma.GetReport(subject).Contains("100/100"), "healthy organs and limbs are hidden");
            var organs = body.GetBodyOrgans(subject).Where(o => entities.HasComponent<TraumaOrganComponent>(o.Id)).ToArray();
            Check(organs.Length >= 6, "medical organs have health pools");
            var heart = organs.First(o => entities.GetComponent<TraumaOrganComponent>(o.Id).Kind == TraumaKind.Heart).Id;
            var health = entities.GetComponent<TraumaOrganComponent>(heart);
            var torso = body.GetBodyChildren(subject).First(o => o.Component.PartType == BodyPartType.Torso).Id;
            Check(health.CurrentHealth == 100 && health.MaximumHealth == 100, "fresh heart starts at 100/100");
            trauma.SetOrganDamage(heart, 100);
            Check(health.MaximumHealth >= 15 && health.MaximumHealth <= 75 && health.CurrentHealth == 0, "full organ failure loses 25 to 85 max health");
            Check(!entities.GetComponent<OrganComponent>(heart).Enabled, "failed organ is disabled");
            var maximum = health.MaximumHealth;
            for (var i = 0; i < 5; i++)
            {
                trauma.SetOrganDamage(heart, 100);
                var ev = new OrganEnableChangedEvent(false);
                entities.EventBus.RaiseLocalEvent(heart, ref ev);
            }
            Check(health.MaximumHealth == maximum, "repeated disabled updates do not charge another failure");
            trauma.Repair(subject, torso, TraumaKind.Heart);
            Check(health.MaximumHealth == maximum && health.CurrentHealth == maximum, "surgery restores current health only to worn maximum");
            Check(entities.GetComponent<OrganComponent>(heart).Enabled, "surgery re-enables the repaired organ");
            trauma.SetOrganDamage(heart, 100);
            Check(health.MaximumHealth < maximum, "a new failure after repair causes new permanent loss");
            health.MaximumHealth = 15;
            health.FailureRecorded = true;
            trauma.SetOrganDamage(heart, 15);
            trauma.Repair(subject, torso, TraumaKind.Heart);
            Check(health.CurrentHealth == 15 && health.MaximumHealth == 15, "85-point example repairs to fifteen of fifteen");
            trauma.Refresh(subject, entities.GetComponent<TraumaBodyComponent>(subject));
            Check(entities.GetComponent<TraumaBodyComponent>(subject).HeartDamage == 85f,
                "repaired 15/15 heart retains an 85 percent loss of original capacity");
            Check(Math.Abs(entities.GetComponent<TraumaBodyComponent>(subject).AppliedHeart - .575f) < .0001f,
                "worn repaired heart still lowers critical threshold");
            var thresholds = entities.System<MobThresholdSystem>();
            var alert = thresholds.GetHealthAlertPercentage(subject, MobState.Alive, FixedPoint2.Zero, FixedPoint2.Zero);
            Check(Math.Abs(alert.Float() - .425f) < .011f,
                "worn heart shows lost capacity on the normal health scale without body damage");
            var critical = thresholds.GetThresholdForState(subject, MobState.Critical);
            Check(Math.Abs(thresholds.GetHealthAlertPercentage(subject, MobState.Alive, critical, FixedPoint2.New(1)).Float() - 1f) < .011f,
                "health indicator reaches fully depleted at the actual reduced critical threshold");
            Check(thresholds.GetHealthAlertPercentage(subject, MobState.Critical, critical, FixedPoint2.New(.25)) == FixedPoint2.New(.25),
                "critical-state alert behavior is unchanged");
            var report = trauma.GetReport(subject);
            Check(report.Contains("15/15") && !report.Contains(" max") && !report.Contains("HP =") && !report.Contains("100/100"), "scanner shows worn health without redundant labels or healthy parts");
            var node = serialization.WriteValue(health, alwaysWrite: true, notNullableOverride: true);
            var restored = serialization.Read<TraumaOrganComponent>(node, notNullableOverride: true);
            Check(restored.MaximumHealth == 15 && restored.Damage == health.Damage, "organ max health survives serialization");
            Check(body.RemoveOrgan(heart), "worn heart can be removed");
            Check(health.MaximumHealth == 15, "removal does not add wear");
            var recipientTorso = body.GetBodyChildren(recipient).First(o => o.Component.PartType == BodyPartType.Torso).Id;
            var otherHeart = body.GetBodyOrgans(recipient).First(o => entities.TryGetComponent<TraumaOrganComponent>(o.Id, out var c) && c.Kind == TraumaKind.Heart).Id;
            Check(body.RemoveOrgan(otherHeart), "recipient heart removed");
            Check(body.InsertOrgan(recipientTorso, heart, "heart"), "worn heart transplanted");
            trauma.Refresh(recipient, entities.GetComponent<TraumaBodyComponent>(recipient));
            Check(health.MaximumHealth == 15 && health.CurrentHealth == 15, "used transplant keeps permanent health history");
            var fresh = entities.SpawnEntity(entities.GetComponent<MetaDataComponent>(otherHeart).EntityPrototype!.ID, coords);
            Check(body.InsertOrgan(torso, fresh, "heart"), "fresh replacement heart inserted");
            trauma.Refresh(subject, entities.GetComponent<TraumaBodyComponent>(subject));
            Check(entities.GetComponent<TraumaOrganComponent>(fresh).MaximumHealth == 100, "fresh replacement restores full maximum");
            health.MaximumHealth = 0;
            health.FailureRecorded = true;
            trauma.SetOrganDamage(heart, 0);
            Check(health.CurrentHealth == 0 && !entities.GetComponent<OrganComponent>(heart).Enabled, "zero-max organ cannot be healed into working state");
            Check(!entities.System<TraumaSurgerySystem>().NeedsRepair(recipient, recipientTorso, TraumaKind.Heart), "destroyed organ requires replacement rather than futile repair");
            var arm = body.GetBodyChildren(subject).First(o => o.Component.PartType == BodyPartType.Arm).Id;
            var armComp = entities.GetComponent<BodyPartComponent>(arm);
            var limb = entities.GetComponent<TraumaLimbComponent>(arm);
            trauma.Fracture(arm);
            maximum = limb.MaximumHealth;
            Check(maximum > 0 && maximum <= 15 && !armComp.Enabled && trauma.GetLimbHealth(arm, limb) == 0, "limb disable caps remaining capacity at fifteen and has zero current health");
            trauma.Fracture(arm);
            Check(limb.MaximumHealth == maximum, "repeat fracture while disabled does not reroll wear");
            trauma.Repair(subject, arm, TraumaKind.Fracture);
            Check(limb.MaximumHealth == maximum, "bone repair alone does not charge another failure");
            trauma.Repair(subject, arm, TraumaKind.LimbDamage);
            Check(limb.MaximumHealth == maximum && trauma.GetLimbHealth(arm, limb) == maximum && armComp.Enabled, "limb surgery repairs to its worn maximum and restores use");
            trauma.Fracture(arm);
            Check(limb.MaximumHealth < maximum, "a subsequent genuine limb failure adds wear");
            trauma.Repair(subject, arm, TraumaKind.Fracture);
            trauma.Repair(subject, arm, TraumaKind.LimbDamage);
            maximum = limb.MaximumHealth;
            var slot = armComp.ParentSlot!.Value.Id;
            var containers = entities.System<SharedContainerSystem>();
            Check(containers.TryGetContainingContainer((arm, null, null), out var container) && containers.Remove(arm, container), "worn limb detached");
            Check(limb.MaximumHealth == maximum, "limb removal does not add wear");
            var newArm = entities.SpawnEntity(entities.GetComponent<MetaDataComponent>(arm).EntityPrototype!.ID, coords);
            Check(body.AttachPart(torso, slot, newArm), "fresh replacement limb attached");
            trauma.Refresh(subject, entities.GetComponent<TraumaBodyComponent>(subject));
            Check(entities.GetComponent<TraumaLimbComponent>(newArm).MaximumHealth == 100, "new limb restores full maximum");
            var savedLimb = serialization.Read<TraumaLimbComponent>(serialization.WriteValue(limb, alwaysWrite: true, notNullableOverride: true), notNullableOverride: true);
            Check(savedLimb.MaximumHealth == maximum && savedLimb.OriginalFailureThreshold == limb.OriginalFailureThreshold, "limb wear survives serialization");
            foreach (var entity in new[] { subject, recipient, otherHeart, arm }) entities.QueueDeleteEntity(entity);

        });
        await pair.CleanReturnAsync();
    }
}
