using System.Collections.Generic;
using System.Linq;
using Content.Server._ReclamationProject.Medical.BioMonitor;
using Content.Server.Traits;
using Content.Shared._ReclamationProject.Medical.BioMonitor;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Explosion;
using Robust.Shared.Containers;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - Pip-Boy bio-monitor: bullet embed chances, fragments landing in the aimed limb,
/// the per-limb state the window shows, removal times (hands / tools / skilled) and the Field Medic perk.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class BioMonitorTests
{
    [Test]
    public async Task EmbedChancesMatchDesign()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        var factory = pair.Server.ResolveDependency<IComponentFactory>();

        await pair.Server.WaitAssertion(() =>
        {
            float Chance(string id, out string fragment)
            {
                Assert.That(prototypes.Index<EntityPrototype>(id).TryGetComponent<BulletEmbedChanceComponent>(out var comp, factory),
                    Is.True, $"{id} should be able to embed");
                fragment = comp!.Fragment;
                return comp.Chance;
            }

            Assert.That(Chance("N14Bullet10mm", out var pistol), Is.EqualTo(0.2f));
            Assert.That(pistol, Is.EqualTo("ReclamationBulletFragmentPistol"));
            Assert.That(Chance("N14Bullet9mm", out _), Is.EqualTo(0.2f));
            Assert.That(Chance("N14Bullet556", out var rifle), Is.EqualTo(0.15f));
            Assert.That(rifle, Is.EqualTo("ReclamationBulletFragmentRifle"));
            Assert.That(Chance("N14Bullet308", out _), Is.EqualTo(0.15f));
            // Spread pellets inherit from their base pellet.
            Assert.That(Chance("PelletShotgun12Spread", out var pellet), Is.EqualTo(0.25f));
            Assert.That(pellet, Is.EqualTo("ReclamationBulletFragmentPellet"));
            Assert.That(Chance("PelletShotgun20Spread", out _), Is.EqualTo(0.25f));

            // Lasers are hitscan (no projectile entity), so they can never embed.

            var perk = prototypes.Index<TraitPrototype>("ReclamationFieldMedic");
            Assert.That(perk.Points, Is.EqualTo(-5), "Field Medic costs 5 points.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BulletFragmentsShowInTheRightLimbAndRemovalTimesFollowSkill()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var monitor = em.System<BioMonitorSystem>();
            var containers = em.System<SharedContainerSystem>();
            var patient = em.SpawnTraumaPatient(map.GridCoords);
            var shooter = em.SpawnTraumaPatient(map.GridCoords);

            // Aim at the left arm and make the roll certain.
            em.EnsureComponent<TargetingComponent>(shooter).Target = TargetBodyPart.LeftArm;
            var bullet = em.SpawnEntity("N14Bullet10mm", map.GridCoords);
            em.GetComponent<BulletEmbedChanceComponent>(bullet).Chance = 1f;
            var hit = new ProjectileHitEvent(new DamageSpecifier(), patient, shooter);
            em.EventBus.RaiseLocalEvent(bullet, ref hit);

            var state = monitor.BuildState(patient, patient, BioMonitorSource.PipBoy);
            var arm = state.Parts.Single(p => p.Part == (ushort) TargetBodyPart.LeftArm);
            Assert.That(arm.Missing, Is.False);
            Assert.That(arm.Embedded, Has.Count.EqualTo(1), "The fragment should be stuck in the aimed limb.");
            var fragment = arm.Embedded[0];
            Assert.That(fragment.Prototype, Is.EqualTo("ReclamationBulletFragmentPistol"));
            Assert.That(fragment.Deep, Is.False);
            Assert.That(fragment.Seconds, Is.EqualTo(6f), "Bare hands take twice the 3 s base time.");
            // It's inside the body (hidden container), not stuck on the outside where anyone could grab it.
            var fragmentUid = em.GetEntity(fragment.Item);
            Assert.That(containers.IsEntityInContainer(fragmentUid), Is.True);
            Assert.That(em.HasComponent<EmbeddableProjectileComponent>(fragmentUid), Is.False);
            Assert.That(state.Parts.Where(p => p.Part != (ushort) TargetBodyPart.LeftArm).All(p => p.Embedded.Count == 0));

            // A losing roll leaves nothing.
            var miss = em.SpawnEntity("N14Bullet10mm", map.GridCoords);
            em.GetComponent<BulletEmbedChanceComponent>(miss).Chance = 0f;
            var missHit = new ProjectileHitEvent(new DamageSpecifier(), patient, shooter);
            em.EventBus.RaiseLocalEvent(miss, ref missHit);
            Assert.That(monitor.BuildState(patient, patient, BioMonitorSource.PipBoy).Parts.Sum(p => p.Embedded.Count), Is.EqualTo(1));

            // A deep rifle round in the chest needs a hemostat, even for a skilled medic.
            var rifle = em.SpawnEntity("ReclamationBulletFragmentRifle", map.GridCoords);
            Assert.That(monitor.EmbedFragment(rifle, patient, TargetBodyPart.Torso, shooter), Is.True);

            // Bleeding: unskilled hands (no perk, low INT) risk 50% heavy bleeding.
            Assert.That(monitor.BleedRisk(shooter, patient), Is.EqualTo((0.5f, 4f)));

            // Field Medic perk = skilled: shallow fragments come out at tool speed.
            Assert.That(monitor.IsSkilled(patient), Is.False);
            em.System<TraitSystem>().AddTrait(patient, prototypes.Index<TraitPrototype>("ReclamationFieldMedic"));
            Assert.That(monitor.IsSkilled(patient), Is.True);

            // Skilled medic: 25% normal bleed on themselves, none on someone else.
            Assert.That(monitor.BleedRisk(patient, patient), Is.EqualTo((0.25f, 2f)));
            Assert.That(monitor.BleedRisk(patient, shooter), Is.EqualTo((0f, 2f)));

            state = monitor.BuildState(patient, patient, BioMonitorSource.Intelligence);
            Assert.That(state.ViewerSkilled, Is.True);
            Assert.That(state.Parts.Single(p => p.Part == (ushort) TargetBodyPart.LeftArm).Embedded[0].BleedRisk, Is.EqualTo(0.25f));
            Assert.That(state.Parts.Single(p => p.Part == (ushort) TargetBodyPart.LeftArm).Embedded[0].Seconds, Is.EqualTo(3f));
            var chest = state.Parts.Single(p => p.Part == (ushort) TargetBodyPart.Torso).Embedded.Single();
            Assert.That(chest.Deep, Is.True);
            Assert.That(chest.NeedsTool, Is.True, "Skill doesn't replace a hemostat for deep rounds.");

            // Holding a hemostat clears that.
            var hemostat = em.SpawnEntity("Hemostat", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(patient, hemostat), Is.True);
            state = monitor.BuildState(patient, patient, BioMonitorSource.Intelligence);
            chest = state.Parts.Single(p => p.Part == (ushort) TargetBodyPart.Torso).Embedded.Single();
            Assert.That(chest.NeedsTool, Is.False);
            Assert.That(chest.Seconds, Is.EqualTo(4f));

            // Pulling one out removes it from the list.
            var item = em.GetEntity(chest.Item);
            monitor.RemoveFragment((item, em.GetComponent<BulletFragmentComponent>(item)), patient);
            Assert.That(containers.TryGetContainingContainer(item, out var holder) && holder.ID == BioMonitorSystem.FragmentContainer,
                Is.False, "Pulled-out fragments come out of the body.");
            Assert.That(em.System<SharedHandsSystem>().IsHolding(patient, item), Is.True, "They land in the remover's hand.");
            state = monitor.BuildState(patient, patient, BioMonitorSource.Intelligence);
            Assert.That(state.Parts.Single(p => p.Part == (ushort) TargetBodyPart.Torso).Embedded, Is.Empty);

            // Explosions leave shrapnel (50% per roll, up to 3 rolls per blast): ten big blasts all but guarantee some.
            var before = monitor.BuildState(patient, patient, BioMonitorSource.PipBoy).Parts.Sum(p => p.Embedded.Count);
            for (var i = 0; i < 10; i++)
            {
                var big = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 80);
                var blast = new BeforeExplodeEvent(big, "Default", new List<EntityUid>());
                em.EventBus.RaiseLocalEvent(patient, ref blast);
            }
            state = monitor.BuildState(patient, patient, BioMonitorSource.PipBoy);
            Assert.That(state.Parts.SelectMany(p => p.Embedded).Count(e => e.Prototype == "ReclamationShrapnelFragment"),
                Is.GreaterThan(0), "Blasts should leave shrapnel.");
            Assert.That(state.Parts.Sum(p => p.Embedded.Count), Is.GreaterThan(before));

            // Fire bombs don't.
            var fireCount = state.Parts.Sum(p => p.Embedded.Count);
            for (var i = 0; i < 10; i++)
            {
                var fire = new BeforeExplodeEvent(new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Heat"), 80), "FireBomb", new List<EntityUid>());
                em.EventBus.RaiseLocalEvent(patient, ref fire);
            }
            Assert.That(monitor.BuildState(patient, patient, BioMonitorSource.PipBoy).Parts.Sum(p => p.Embedded.Count), Is.EqualTo(fireCount));

            // Vitals are filled in for a living human.
            Assert.That(state.Dead, Is.False);
            Assert.That(state.Pulse, Is.GreaterThan(0));
            Assert.That(state.Blood, Is.GreaterThan(90f));

            em.DeleteEntity(patient);
            em.DeleteEntity(shooter);
        });
        await pair.CleanReturnAsync();
    }
}
