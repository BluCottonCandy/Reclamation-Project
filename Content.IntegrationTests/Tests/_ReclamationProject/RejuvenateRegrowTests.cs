using System.Linq;
using Content.Server.Administration.Systems;
using Content.Server.Traits;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Hands.Components;
using Content.Shared.Traits;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>#Reclamation Project - admin Rejuvenate regrows missing limbs and organs, but not replacements.</summary>
[TestFixture, NonParallelizable]
public sealed class RejuvenateRegrowTests
{
    [Test]
    public async Task RejuvenateRegrowsMissingArmHandAndHeart()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var body = em.System<SharedBodySystem>();
            var containers = em.System<SharedContainerSystem>();
            var hands = em.GetComponent<HandsComponent>(patient).Hands.Count;

            // Lose the left arm (and hand with it) and the heart.
            em.System<TraitSystem>().AddTrait(patient, prototypes.Index<TraitPrototype>("ReclamationAmputatedLeftArm"));
            Assert.That(body.GetBodyChildrenOfType(patient, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left), Is.Empty);
            var heart = body.GetBodyOrgans(patient).Single(o => o.Component.SlotId == "heart").Id;
            Assert.That(containers.TryGetContainingContainer((heart, null, null), out var heartSlot), Is.True);
            containers.Remove(heart, heartSlot!, force: true);
            em.DeleteEntity(heart);
            Assert.That(body.GetBodyOrgans(patient).Any(o => o.Component.SlotId == "heart"), Is.False);
            var damage = new DamageSpecifier();
            damage.DamageDict["Blunt"] = 40;
            em.System<DamageableSystem>().TryChangeDamage(patient, damage, true);

            em.System<RejuvenateSystem>().PerformRejuvenate(patient);

            Assert.That(body.GetBodyChildrenOfType(patient, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left).Count(), Is.EqualTo(1));
            Assert.That(body.GetBodyChildrenOfType(patient, BodyPartType.Hand, symmetry: BodyPartSymmetry.Left).Count(), Is.EqualTo(1));
            Assert.That(body.GetBodyOrgans(patient).Count(o => o.Component.SlotId == "heart"), Is.EqualTo(1));
            Assert.That(em.GetComponent<HandsComponent>(patient).Hands.Count, Is.EqualTo(hands), "The hand works again.");
            Assert.That(em.GetComponent<DamageableComponent>(patient).TotalDamage.Float(), Is.Zero);

            // Nothing doubles up on a second rejuvenate.
            em.System<RejuvenateSystem>().PerformRejuvenate(patient);
            Assert.That(body.GetBodyChildrenOfType(patient, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left).Count(), Is.EqualTo(1));
            Assert.That(body.GetBodyOrgans(patient).Count(o => o.Component.SlotId == "heart"), Is.EqualTo(1));
            em.DeleteEntity(patient);
        });
        await pair.CleanReturnAsync();
    }
}
