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
    public async Task TissueRepairsRemainAvailableAndHealHeadChestAndLimbs()
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
            var body = em.System<SharedBodySystem>();
            var parts = body.GetBodyChildren(patient).ToArray();
            foreach (var type in new[] { BodyPartType.Head, BodyPartType.Torso, BodyPartType.Arm, BodyPartType.Hand, BodyPartType.Leg, BodyPartType.Foot })
            {
                var part = parts.First(p => p.Item2.PartType == type).Id;
                var valid = new SurgeryValidEvent(patient, part);
                em.EventBus.RaiseLocalEvent(procedure, ref valid);
                Check(!valid.Cancelled, $"{type} tissue repair missing before damage.");
                EntityUid? eye = null;
                if (type == BodyPartType.Head)
                {
                    eye = body.GetPartOrgans(part).First(o =>
                        em.TryGetComponent<TraumaOrganComponent>(o.Id, out var organ) && organ.Kind == TraumaKind.Eyes).Id;
                    trauma.SetOrganDamage(eye.Value, 25);
                }
                damageSystem.TryChangeDamage(part, new DamageSpecifier { DamageDict = new() { ["Blunt"] = FixedPoint2.New(2) } }, ignoreResistances: true, doPartDamage: false, canSever: false);
                Check(surgery.NeedsRepair(patient, part, TraumaKind.LimbDamage), $"{type} damage is not repairable.");
                var health = em.GetComponent<TraumaLimbComponent>(part);
                var maximum = health.MaximumHealth;
                trauma.Repair(patient, part, TraumaKind.LimbDamage, surgical: true);
                Check(em.GetComponent<DamageableComponent>(part).TotalDamage == FixedPoint2.Zero, $"{type} tissue repair did not heal.");
                Check(health.MaximumHealth == maximum, $"{type} repair changed permanent health capacity.");
                Check(!surgery.NeedsRepair(patient, part, TraumaKind.LimbDamage), $"{type} remains incomplete after repair.");
                if (eye is { } eyeId)
                    Check(em.GetComponent<TraumaOrganComponent>(eyeId).Damage == 25, "Head tissue repair also healed an injured eye.");
            }
            em.DeleteEntity(procedure);
        }
        finally { em.DeleteEntity(map); }
    }
}
