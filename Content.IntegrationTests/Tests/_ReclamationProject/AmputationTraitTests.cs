using System.Linq;
using Content.Server.Traits;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class AmputationTraitTests
{
    [TestCase("LeftArm", BodyPartType.Arm, BodyPartType.Hand, BodyPartSymmetry.Left)]
    [TestCase("RightArm", BodyPartType.Arm, BodyPartType.Hand, BodyPartSymmetry.Right)]
    [TestCase("LeftLeg", BodyPartType.Leg, BodyPartType.Foot, BodyPartSymmetry.Left)]
    [TestCase("RightLeg", BodyPartType.Leg, BodyPartType.Foot, BodyPartSymmetry.Right)]
    public async Task PerkRemovesSelectedLimbAndAwardsFivePoints(string suffix, BodyPartType part,
        BodyPartType child, BodyPartSymmetry side)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var body = em.System<SharedBodySystem>();
            var removed = body.GetBodyChildrenOfType(patient, part, symmetry: side)
                .Concat(body.GetBodyChildrenOfType(patient, child, symmetry: side)).Select(p => p.Id).ToArray();
            var trait = prototypes.Index<TraitPrototype>("ReclamationAmputated" + suffix);
            Assert.That(trait.Points, Is.EqualTo(5));
            Assert.That(trait.Category.ToString(), Is.EqualTo("Physical"));
            em.System<TraitSystem>().AddTrait(patient, trait);
            Assert.That(body.GetBodyChildrenOfType(patient, part, symmetry: side), Is.Empty);
            Assert.That(body.GetBodyChildrenOfType(patient, child, symmetry: side), Is.Empty);
            Assert.That(removed.All(p => !em.EntityExists(p)), Is.True, "No detached limbs should be left behind.");
            var opposite = side == BodyPartSymmetry.Left ? BodyPartSymmetry.Right : BodyPartSymmetry.Left;
            Assert.That(body.GetBodyChildrenOfType(patient, part, symmetry: opposite).Count(), Is.EqualTo(1));
            Assert.That(em.GetComponent<DamageableComponent>(patient).TotalDamage.Float(), Is.Zero,
                "Roundstart amputations must be healed, without combat damage.");
            // Applying the perk again is safe, including local test resets.
            em.System<TraitSystem>().AddTrait(patient, trait);
            if (part == BodyPartType.Leg)
            {
                var cane = em.SpawnEntity("Cane", map.GridCoords);
                Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(patient, cane), Is.True);
                Assert.That(em.System<StandingStateSystem>().Stand(patient), Is.True);
            }
            em.DeleteEntity(patient);
        });
        await pair.CleanReturnAsync();
    }
}
