using System.Linq;
using Content.Server._ReclamationProject.Institute.Courser;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Flash.Components;
using Content.Server.Jobs;
using Content.Shared._Misfits.Traits;
using Content.Shared._ReclamationProject.Institute.Augment;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Customization.Systems;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class CourserSpeciesTest
{
    [Test]
    public async Task CourserIsAToughHumanLookingSynth()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var courser = entities.SpawnEntity("MobCourser", map.GridCoords);

            // Looks human: human sprites and customisation, own (human-looking) body.
            Assert.That(entities.GetComponent<HumanoidAppearanceComponent>(courser).Species.Id, Is.EqualTo("Courser"));
            var species = protos.Index<Content.Shared.Humanoid.Prototypes.SpeciesPrototype>("Courser");
            Assert.That(species.SpriteSet, Is.EqualTo("MobHumanSprites"), "Courser doesn't use human sprites");
            Assert.That(species.WhitelistRequired, Is.True, "Courser race isn't whitelisted");

            // C-27 health: crit 200, dead 400.
            var thresholds = entities.System<MobThresholdSystem>();
            Assert.That(thresholds.TryGetThresholdForState(courser, MobState.Critical, out var crit) && crit == 200, "crit isn't 200");
            Assert.That(thresholds.TryGetThresholdForState(courser, MobState.Dead, out var dead) && dead == 400, "dead isn't 400");

            // Radiation does nothing.
            var damageable = entities.System<DamageableSystem>();
            damageable.TryChangeDamage(courser, new DamageSpecifier { DamageDict = { ["Radiation"] = 50 } });
            Assert.That(entities.GetComponent<DamageableComponent>(courser).TotalDamage.Float(), Is.Zero, "Courser took radiation damage");

            // Eats and drinks like a human (has a stomach), never suffocates, bleeding doesn't hurt.
            Assert.That(entities.System<BodySystem>().TryGetBodyOrganComponents<StomachComponent>(courser, out var stomachs)
                && stomachs.Count > 0, "Courser has no stomach");
            Assert.That(entities.GetComponent<RespiratorComponent>(courser).SuffocationThreshold, Is.LessThan(-100f), "Courser can suffocate");
            var blood = entities.GetComponent<BloodstreamComponent>(courser);
            Assert.That(new DamageSpecifier(blood.BloodlossDamage).GetTotal().Float(), Is.Zero, "blood loss hurts a Courser");

            // Body parts and organs pass for human while attached and give themselves away once out.
            var bodies = entities.System<BodySystem>();
            var arm = bodies.GetBodyChildrenOfType(courser, BodyPartType.Arm).First().Id;
            Assert.That(entities.GetComponent<MetaDataComponent>(arm).EntityName, Does.Not.Contain("synthetic"), "attached arm gives it away");
            var heart = bodies.GetBodyOrganComponents<HeartComponent>(courser).First().Comp.Owner;
            Assert.That(entities.GetComponent<MetaDataComponent>(heart).EntityName, Does.Not.Contain("synthetic"), "heart gives it away while inside");
            Assert.That(bodies.RemoveOrgan(heart), Is.True);
            Assert.That(entities.GetComponent<MetaDataComponent>(heart).EntityName, Is.EqualTo("synthetic heart"), "removed heart didn't reveal itself");
            entities.System<Robust.Shared.Containers.SharedContainerSystem>().TryRemoveFromContainer(arm, force: true);
            Assert.That(entities.GetComponent<MetaDataComponent>(arm).EntityName, Does.StartWith("synthetic"), "cut-off arm didn't reveal itself");

            // Courser perks in every job.
            Assert.That(entities.GetComponent<InstituteAugmentComponent>(courser).DamageMultiplier, Is.EqualTo(0.70f).Within(0.001f));
            Assert.That(entities.GetComponent<GibModifierComponent>(courser).Ungibbable, Is.True);
            Assert.That(entities.HasComponent<FlashImmunityComponent>(courser));
            Assert.That(entities.System<NpcFactionSystem>().IsMember(courser, "Institute"), "Courser isn't Institute");

            // A Minutemen job replaces factions; the Courser gets Institute back after spawning.
            var job = protos.Index<JobPrototype>("MinutemenCaptain");
            foreach (var special in job.Special)
                special.AfterEquip(courser);
            Assert.That(entities.System<NpcFactionSystem>().IsMember(courser, "Institute"), Is.False, "test setup: job didn't replace factions");
            entities.System<CourserSynthSystem>().EnsureInstitute(courser);
            Assert.That(entities.System<NpcFactionSystem>().IsMember(courser, "Institute"), "Courser lost Institute in another job");
            Assert.That(entities.System<NpcFactionSystem>().IsMember(courser, "Minutemen"), "Courser lost their job's faction");

            // The Courser job is locked to the race.
            var courserJob = protos.Index<JobPrototype>("InstituteCourser");
            Assert.That(courserJob.Requirements?.OfType<CharacterSpeciesRequirement>()
                .Any(r => !r.Inverted && r.Species.Count == 1 && r.Species[0] == "Courser") == true, "Courser job isn't locked to the race");

            // Human-only perks include Coursers (e.g. Unnatural Regeneration).
            var trait = protos.Index<TraitPrototype>("N14UnnaturalReg");
            Assert.That(trait.Requirements.OfType<CharacterSpeciesRequirement>()
                .Any(r => r.Species.Contains("Human") && r.Species.Contains("Courser")), "human perk doesn't include Coursers");
        });

        await pair.CleanReturnAsync();
    }
}
