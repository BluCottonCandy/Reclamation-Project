using System.Collections.Generic;
using System.Linq;
using Content.Server._ReclamationProject.Institute.Courser;
using Content.Shared._NC.Sponsor;
using Content.Shared._ReclamationProject.Institute.Augment;
using Content.Shared._ReclamationProject.Institute.Nanites;
using Content.Shared.CCVar;
using Content.Shared.Customization.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - Gen 3 Synth race: a weaker Courser that passes as human, can take any job a human can,
/// always counts as Institute, and is unlocked by the Institute Synth job whitelist.
/// </summary>
[TestFixture]
public sealed class Gen3SynthTest
{
    [Test]
    public async Task Gen3SynthIsAWeakerHiddenCourser()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            // The race: whitelisted, unlocked by the Institute Synth job, shows as Human.
            var species = prototypes.Index<SpeciesPrototype>("Gen3Synth");
            Assert.That(species.WhitelistRequired, Is.True, "Gen 3 Synth isn't whitelisted");
            Assert.That(species.JobWhitelistUnlock?.Id, Is.EqualTo("InstituteSynth"));
            Assert.That(species.SpriteSet, Is.EqualTo("MobHumanSprites"), "doesn't use human sprites");
            Assert.That(entities.System<SharedHumanoidAppearanceSystem>().GetDisplaySpecies("Gen3Synth"), Is.EqualTo("Human"),
                "others can see it's a Gen 3 Synth");

            var synth = entities.SpawnEntity("MobGen3Synth", map.GridCoords);

            // Weaker than a Courser: crit 200 / dead 300, stamina 175.
            var thresholds = entities.System<MobThresholdSystem>();
            Assert.That(thresholds.TryGetThresholdForState(synth, MobState.Critical, out var crit) && crit == 200, "crit isn't 200");
            Assert.That(thresholds.TryGetThresholdForState(synth, MobState.Dead, out var dead) && dead == 300, "dead isn't 300");
            Assert.That(entities.GetComponent<StaminaComponent>(synth).CritThreshold, Is.EqualTo(175f), "stamina isn't 175");

            // 15% less damage, +30% melee, slow nanites.
            Assert.That(entities.GetComponent<InstituteAugmentComponent>(synth).DamageMultiplier, Is.EqualTo(0.85f).Within(0.001f));
            var punch = entities.System<SharedMeleeWeaponSystem>().GetDamage(synth, synth);
            Assert.That(punch.GetTotal().Float(), Is.EqualTo(13f).Within(0.01f), "punch isn't +30%");
            var nanites = entities.GetComponent<NaniteRepairComponent>(synth);
            Assert.That(nanites.HealPerSecond, Is.EqualTo(0.5f).Within(0.001f), "nanite heal isn't 0.5 per type");
            Assert.That(nanites.BonePerSecond, Is.EqualTo(5f).Within(0.001f), "bones don't take 20 s");

            // Radiation does nothing; none of the Courser combat perks.
            entities.System<DamageableSystem>().TryChangeDamage(synth, new DamageSpecifier { DamageDict = { ["Radiation"] = 50 } });
            Assert.That(entities.GetComponent<DamageableComponent>(synth).TotalDamage.Float(), Is.Zero, "took radiation damage");
            Assert.That(entities.HasComponent<Content.Server.Flash.Components.FlashImmunityComponent>(synth), Is.False, "has the Courser's flash immunity");
            Assert.That(entities.HasComponent<Content.Shared.Weapons.Reflect.ReflectComponent>(synth), Is.False, "has the Courser's ricochet");

            // Always Institute, so it shows on the Institute tablet map, even after a job takes the faction away.
            var factions = entities.System<NpcFactionSystem>();
            Assert.That(factions.IsMember(synth, "Institute"), "isn't Institute");
            factions.ClearFactions(synth);
            entities.System<CourserSynthSystem>().EnsureInstitute(synth);
            Assert.That(factions.IsMember(synth, "Institute"), "didn't get the Institute faction back after a job removed it");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Gen3SynthCanTakeHumanJobsAndTheSynthJob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var sponsors = server.ResolveDependency<ISharedSponsorManager>();

        await server.WaitAssertion(() =>
        {
            var requirements = entities.System<CharacterRequirementsSystem>();
            var synth = HumanoidCharacterProfile.DefaultWithSpecies("Gen3Synth");
            var human = HumanoidCharacterProfile.DefaultWithSpecies("Human");

            // Every species list that has Courser (and Human) also has Gen 3 Synth, except the Courser-only job.
            var missing = new List<string>();
            foreach (var job in prototypes.EnumeratePrototypes<JobPrototype>())
            {
                foreach (var req in job.Requirements ?? new())
                {
                    if (req is CharacterSpeciesRequirement species && species.Species.Contains("Human")
                        && species.Species.Contains("Courser") && !species.Species.Contains("Gen3Synth"))
                        missing.Add(job.ID);
                }
            }
            Assert.That(missing, Is.Empty, "jobs that let Humans and Coursers in but not Gen 3 Synths: " + string.Join(", ", missing));

            bool SpeciesOk(string jobId, HumanoidCharacterProfile profile)
            {
                var job = prototypes.Index<JobPrototype>(jobId);
                var speciesReqs = (job.Requirements ?? new()).OfType<CharacterSpeciesRequirement>().Cast<CharacterRequirement>().ToList();
                return requirements.CheckRequirementsValid(speciesReqs, job, profile, new Dictionary<string, TimeSpan>(), true, job,
                    entities, prototypes, config, sponsors, out _);
            }

            Assert.That(SpeciesOk("InstituteSynth", synth), Is.True, "a Gen 3 Synth can't take the Institute Synth job");
            Assert.That(SpeciesOk("InstituteSynth", human), Is.False, "a human can take the Institute Synth job");
            Assert.That(SpeciesOk("InstituteCourser", synth), Is.False, "a Gen 3 Synth can take the Courser job");
            Assert.That(prototypes.Index<DepartmentPrototype>("Institute").Roles, Does.Contain((ProtoId<JobPrototype>) "InstituteSynth"));
        });

        await pair.CleanReturnAsync();
    }
}
