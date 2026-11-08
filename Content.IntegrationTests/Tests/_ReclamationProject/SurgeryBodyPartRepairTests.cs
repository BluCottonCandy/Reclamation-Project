using System;
using System.Linq;
using System.Numerics;
using Content.Server._ReclamationProject.Medical;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using System.Threading.Tasks;
namespace Content.IntegrationTests.Tests._ReclamationProject;
[TestFixture]
public sealed class SurgeryBodyPartRepairTests
{
    [Test]
    public async Task TissueRepairsRemainAvailableAndHealChestAndLimbs()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() => Verify(pair.Server.ResolveDependency<IEntityManager>()));
        await pair.CleanReturnAsync();
    }
    public static void Verify(IEntityManager em)
    {
        var map = em.System<SharedMapSystem>().CreateMap();
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        try
        {
            var patient = em.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var trauma = em.System<TraumaSystem>();
            trauma.InitializeBody(patient);
            var medical = em.GetComponent<TraumaBodyComponent>(patient);
            medical.SuppressInjuryRolls = true;
            var surgery = em.System<TraumaSurgerySystem>();
            var damageSystem = em.System<DamageableSystem>();
            var procedure = em.SpawnEntity("ReclamationSurgeryRepairLimbDamage", MapCoordinates.Nullspace);
            var parts = em.System<SharedBodySystem>().GetBodyChildren(patient).ToArray();
            foreach (var type in new[] { BodyPartType.Torso, BodyPartType.Arm, BodyPartType.Hand, BodyPartType.Leg, BodyPartType.Foot })
            {
                var part = parts.First(p => p.Item2.PartType == type).Id;
                var valid = new SurgeryValidEvent(patient, part);
                em.EventBus.RaiseLocalEvent(procedure, ref valid);
                Check(!valid.Cancelled, $"{type} tissue repair missing before damage.");
                damageSystem.TryChangeDamage(part, new DamageSpecifier { DamageDict = new() { ["Blunt"] = FixedPoint2.New(2) } }, ignoreResistances: true, doPartDamage: false, canSever: false);
                Check(surgery.NeedsRepair(patient, part, TraumaKind.LimbDamage), $"{type} damage is not repairable.");
                var health = em.GetComponent<TraumaLimbComponent>(part);
                var maximum = health.MaximumHealth;
                trauma.Repair(patient, part, TraumaKind.LimbDamage, surgical: true);
                Check(em.GetComponent<DamageableComponent>(part).TotalDamage == FixedPoint2.Zero, $"{type} tissue repair did not heal.");
                Check(health.MaximumHealth == maximum, $"{type} repair changed permanent health capacity.");
                Check(!surgery.NeedsRepair(patient, part, TraumaKind.LimbDamage), $"{type} remains incomplete after repair.");
            }
            var head = parts.First(p => p.Item2.PartType == BodyPartType.Head).Id;
            var invalid = new SurgeryValidEvent(patient, head);
            em.EventBus.RaiseLocalEvent(procedure, ref invalid);
            Check(invalid.Cancelled, "Tissue repair unexpectedly includes brain/head surgery.");
        }
        finally { em.DeleteEntity(map); }
    }
}
