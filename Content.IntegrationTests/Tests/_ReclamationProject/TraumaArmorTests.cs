using System.Linq;
using Content.Server._ReclamationProject.Medical;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Armor;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class TraumaArmorTests
{
    [TestCase(0f)]
    [TestCase(.5f)]
    public async Task WornArmorReducesLimbDamageBeforeTraumaRolls(float coefficient)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid bare = default, protectedPatient = default;
        await pair.Server.WaitAssertion(() =>
        {
            bare = em.SpawnTraumaPatient(map.GridCoords);
            protectedPatient = em.SpawnTraumaPatient(map.GridCoords);
            var armor = em.SpawnEntity("N14ClothingOuterCombatArmor", map.GridCoords);
            em.GetComponent<ArmorComponent>(armor).Modifiers = new DamageModifierSet
            {
                Coefficients = new() { ["Blunt"] = coefficient },
            };
            Assert.That(em.System<InventorySystem>().TryEquip(protectedPatient, armor, "outerClothing", force: true), Is.True);
            var body = em.System<SharedBodySystem>();
            var damage = em.System<DamageableSystem>();
            foreach (var patient in new[] { bare, protectedPatient })
                damage.TryChangeDamage(patient, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 40 } },
                    canSever: false, canEvade: false, targetPart: TargetBodyPart.Torso);
            var bareTorso = body.GetBodyChildrenOfType(bare, BodyPartType.Torso).First().Id;
            var armoredTorso = body.GetBodyChildrenOfType(protectedPatient, BodyPartType.Torso).First().Id;
            var unprotectedDamage = em.GetComponent<DamageableComponent>(bareTorso).TotalDamage.Float();
            var protectedDamage = em.GetComponent<DamageableComponent>(armoredTorso).TotalDamage.Float();
            Assert.That(unprotectedDamage, Is.GreaterThan(0));
            Assert.That(protectedDamage, Is.LessThanOrEqualTo(unprotectedDamage * coefficient));
        });
        await pair.RunSeconds(.3f);
        await pair.Server.WaitAssertion(() =>
        {
            if (coefficient == 0)
            {
                Assert.That(em.GetComponent<TraumaBodyComponent>(protectedPatient).InternalBleeding, Is.Zero);
                foreach (var (organ, _) in em.System<SharedBodySystem>().GetBodyOrgans(protectedPatient))
                    if (em.TryGetComponent<TraumaOrganComponent>(organ, out var injury)) Assert.That(injury.Damage, Is.Zero);
            }
            var trauma = em.System<TraumaSystem>();
            em.GetComponent<TraumaBodyComponent>(protectedPatient).InternalBleeding = 0;
            trauma.AddInternalBleeding(protectedPatient, 2.9888888f);
            var report = trauma.GetReport(protectedPatient);
            Assert.That(report, Does.Contain("Internal bleeding: 3 units"));
            Assert.That(report, Does.Not.Contain("2.988"));
            em.DeleteEntity(bare);
            em.DeleteEntity(protectedPatient);
        });
        await pair.CleanReturnAsync();
    }
}
