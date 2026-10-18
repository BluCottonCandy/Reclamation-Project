using System.Linq;
using Content.Server._ReclamationProject.Medical;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class TraumaWalkingSupportTests
{
    [TestCase("Cane", BodyPartType.Leg)]
    [TestCase("WhiteCane", BodyPartType.Foot)]
    public async Task HeldCaneSupportsOneDisabledSideOnly(string prototype, BodyPartType type)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var cane = em.SpawnEntity(prototype, map.GridCoords);
            var body = em.System<SharedBodySystem>();
            var trauma = em.System<TraumaSystem>();
            var standing = em.System<StandingStateSystem>();
            var hands = em.System<SharedHandsSystem>();
            body.UpdateMovementSpeed(patient);
            var movement = em.GetComponent<MovementSpeedModifierComponent>(patient);
            var walkSpeed = movement.CurrentWalkSpeed;
            var sprintSpeed = movement.CurrentSprintSpeed;
            var left = body.GetBodyChildrenOfType(patient, type, symmetry: BodyPartSymmetry.Left).First().Id;
            var right = body.GetBodyChildrenOfType(patient, type, symmetry: BodyPartSymmetry.Right).First().Id;
            trauma.Fracture(left);
            Assert.That(standing.Stand(patient), Is.False);
            Assert.That(hands.TryPickupAnyHand(patient, cane), Is.True);
            Assert.That(standing.IsDown(patient), Is.False, "A held cane must let the patient stand.");
            Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(walkSpeed * .5f).Within(.001f));
            Assert.That(movement.CurrentSprintSpeed, Is.EqualTo(sprintSpeed * .5f).Within(.001f));
            Assert.That(em.GetComponent<BodyPartComponent>(left).Enabled, Is.False, "The cane must not heal the limb.");
            Assert.That(hands.TryDrop(patient, cane), Is.True);
            Assert.That(standing.IsDown(patient), Is.True, "Dropping the support must remove its benefit.");
            trauma.Repair(patient, left, Content.Shared._ReclamationProject.Medical.TraumaKind.Fracture);
            Assert.That(standing.Stand(patient), Is.True);
            Assert.That(hands.TryPickupAnyHand(patient, cane), Is.True);
            trauma.Fracture(left);
            Assert.That(standing.IsDown(patient), Is.False, "An already-held cane must support a new injury.");
            Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(walkSpeed * .5f).Within(.001f));
            trauma.Fracture(right);
            Assert.That(standing.Stand(patient), Is.False, "A cane cannot replace both legs.");
            em.DeleteEntity(patient);
            if (em.EntityExists(cane)) em.DeleteEntity(cane);
        });
        await pair.CleanReturnAsync();
    }
}
