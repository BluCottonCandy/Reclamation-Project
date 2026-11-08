using System.Linq;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Body.Components;
using Content.Shared._ReclamationProject.Institute.Nanites;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Atmos;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class NaniteRepairTest
{
    [Test]
    public async Task CourserNanitesRepairEverythingAfterTheWait()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        EntityUid courser = default;
        EntityUid arm = default;

        await server.WaitPost(() =>
        {
            courser = entities.SpawnEntity("MobCourser", map.GridCoords);
            entities.System<TraumaSystem>().InitializeBody(courser);
            var nanites = entities.GetComponent<NaniteRepairComponent>(courser);
            nanites.Delay = TimeSpan.FromSeconds(2);
            nanites.HealPerSecond = 40;
            nanites.CapacityPerSecond = 50;
            nanites.BonePerSecond = 50;
            nanites.OrganPerSecond = 50;
            arm = entities.System<SharedBodySystem>().GetBodyChildrenOfType(courser, Content.Shared.Body.Part.BodyPartType.Arm).First().Id;
            // No origin and no target part: hits every limb as well as the body.
            entities.System<DamageableSystem>().TryChangeDamage(courser, new DamageSpecifier { DamageDict = { ["Blunt"] = 60, ["Heat"] = 30 } });
        });
        await pair.RunTicksSync(5);

        float bodyBefore = 0, armBefore = 0;
        await server.WaitAssertion(() =>
        {
            bodyBefore = entities.GetComponent<DamageableComponent>(courser).TotalDamage.Float();
            armBefore = entities.GetComponent<DamageableComponent>(arm).TotalDamage.Float();
            Assert.That(bodyBefore, Is.GreaterThan(0));
            Assert.That(armBefore, Is.GreaterThan(0));
        });

        // Still inside the wait: nothing healed.
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<DamageableComponent>(courser).TotalDamage.Float(), Is.EqualTo(bodyBefore).Within(0.01f), "healed before the wait");
            // Trauma's own slow natural recovery still ticks on limbs (a few hundredths a second).
            Assert.That(entities.GetComponent<DamageableComponent>(arm).TotalDamage.Float(), Is.EqualTo(armBefore).Within(1f), "limb healed before the wait");
        });

        // After the wait the body and every limb get repaired completely.
        await pair.RunSeconds(8);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<DamageableComponent>(courser).TotalDamage.Float(), Is.Zero, "body not repaired");
            foreach (var (part, _) in entities.System<SharedBodySystem>().GetBodyChildren(courser))
                Assert.That(entities.GetComponent<DamageableComponent>(part).TotalDamage.Float(), Is.Zero, $"{entities.ToPrettyString(part)} not repaired");
            var blood = entities.GetComponent<BloodstreamComponent>(courser);
            var state = $"blood={entities.System<Content.Server.Body.Systems.BloodstreamSystem>().GetBloodLevelPercentage(courser, blood)} " +
                $"bleed={blood.BleedAmount} internal={entities.GetComponent<TraumaBodyComponent>(courser).InternalBleeding} organs=" +
                string.Join(",", entities.System<SharedBodySystem>().GetBodyOrgans(courser)
                    .Where(o => entities.HasComponent<TraumaOrganComponent>(o.Id))
                    .Select(o => $"{entities.GetComponent<TraumaOrganComponent>(o.Id).Damage}/{entities.GetComponent<TraumaOrganComponent>(o.Id).MaximumHealth}")) +
                " limbs=" + string.Join(",", entities.System<SharedBodySystem>().GetBodyChildren(courser)
                    .Where(p => entities.HasComponent<TraumaLimbComponent>(p.Id))
                    .Select(p => $"{entities.GetComponent<TraumaLimbComponent>(p.Id).MaximumHealth}{(entities.HasComponent<FractureComponent>(p.Id) ? "F" : "")}"));
            Assert.That(entities.GetComponent<NaniteRepairComponent>(courser).Repairing, Is.False, "never said it finished: " + state);
        });

        // Scarred organs and limbs get their lost maximum health back.
        EntityUid heart = default;
        float heartScarBefore = 0, armScarBefore = 0;
        await server.WaitPost(() =>
        {
            var body = entities.System<SharedBodySystem>();
            heart = body.GetBodyOrgans(courser).First(o => entities.HasComponent<HeartComponent>(o.Id)).Id;
            // The opening hit can already have failed (and scarred) a part, so measure from here.
            heartScarBefore = entities.GetComponent<TraumaOrganComponent>(heart).HiddenScarLoss;
            entities.GetComponent<TraumaOrganComponent>(heart).MaximumHealth = 60f;
            var limb = entities.GetComponent<TraumaLimbComponent>(arm);
            armScarBefore = limb.HiddenScarLoss;
            var part = entities.GetComponent<Content.Shared.Body.Part.BodyPartComponent>(arm);
            limb.MaximumHealth = 40f;
            foreach (var key in part.IntegrityThresholds.Keys.ToArray())
                part.IntegrityThresholds[key] *= 40f / limb.AppliedMaximumHealth;
            limb.AppliedMaximumHealth = 40f;
            entities.GetComponent<NaniteRepairComponent>(courser).CapacityPerSecond = 50f;
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<TraumaOrganComponent>(heart).MaximumHealth, Is.EqualTo(100f), "heart capacity not restored");
            var limb = entities.GetComponent<TraumaLimbComponent>(arm);
            Assert.That(limb.MaximumHealth, Is.EqualTo(100f), "arm capacity not restored");
            var part = entities.GetComponent<Content.Shared.Body.Part.BodyPartComponent>(arm);
            Assert.That(part.IntegrityThresholds[Content.Shared._Shitmed.Targeting.TargetIntegrity.CriticallyWounded],
                Is.EqualTo(limb.OriginalFailureThreshold).Within(0.01f), "arm thresholds not restored");

            // The scars stay on record and scans still show the lowered maximum, just like a human's.
            var heartScar = entities.GetComponent<TraumaOrganComponent>(heart).HiddenScarLoss;
            Assert.That(heartScar - heartScarBefore, Is.EqualTo(40f).Within(0.01f), "heart scar not kept");
            Assert.That(limb.HiddenScarLoss - armScarBefore, Is.EqualTo(60f).Within(0.01f), "arm scar not kept");
            var report = entities.System<Content.Server._ReclamationProject.Medical.TraumaSystem>().GetReport(courser);
            string Shown(float scar) => "/" + Math.Max(0f, 100f - scar).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " ·";
            Assert.That(report, Does.Contain(Shown(heartScar)), "scan doesn't show the heart scar: " + report);
            Assert.That(report, Does.Contain(Shown(limb.HiddenScarLoss)), "scan doesn't show the arm scar: " + report);
        });

        // A new hit starts the wait over.
        await server.WaitPost(() =>
            entities.System<DamageableSystem>().TryChangeDamage(courser, new DamageSpecifier { DamageDict = { ["Blunt"] = 20 } }));
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<DamageableComponent>(courser).TotalDamage.Float(), Is.GreaterThan(0), "hit didn't reset the wait"));

        await server.WaitAssertion(() =>
        {
            // 95% fire resistance (lasers are ordinary Heat damage and don't use this event).
            var fire = new GetFireProtectionEvent();
            entities.EventBus.RaiseLocalEvent(courser, ref fire);
            Assert.That(fire.Multiplier, Is.EqualTo(0.05f).Within(0.001f));

            // Never breathes, blood loss never counts, and shows as a human.
            Assert.That(entities.HasComponent<RespiratorImmuneComponent>(courser), "Courser still breathes");
            Assert.That(entities.GetComponent<BloodstreamComponent>(courser).BloodlossThreshold, Is.Zero, "blood loss still hurts");
            Assert.That(entities.System<SharedHumanoidAppearanceSystem>().GetDisplaySpecies("Courser"), Is.EqualTo("Human"));

            // Starve them: the nanites top hunger and thirst back up.
            entities.System<HungerSystem>().SetHunger(courser, 0);
            var thirst = entities.GetComponent<ThirstComponent>(courser);
            entities.System<ThirstSystem>().SetThirst(courser, thirst, 0);
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var hunger = entities.GetComponent<HungerComponent>(courser);
            Assert.That(hunger.CurrentHunger, Is.GreaterThanOrEqualTo(hunger.Thresholds[HungerThreshold.Okay]), "Courser got hungry");
            var thirst = entities.GetComponent<ThirstComponent>(courser);
            Assert.That(thirst.CurrentThirst, Is.GreaterThanOrEqualTo(thirst.ThirstThresholds[ThirstThreshold.Okay]), "Courser got thirsty");
        });

        await pair.CleanReturnAsync();
    }
}
