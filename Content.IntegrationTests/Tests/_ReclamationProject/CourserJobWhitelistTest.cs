using System.Collections.Generic;
using Content.Shared._NC.Sponsor;
using Content.Shared.CCVar;
using Content.Shared.Customization.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

// #Reclamation Project - a Courser job whitelist skips the Courser job's hour requirement, but not the race requirement.
[TestFixture]
public sealed class CourserJobWhitelistTest
{
    [Test]
    public async Task WhitelistSkipsHoursButNotRace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var sponsors = server.ResolveDependency<ISharedSponsorManager>();
        var oldTimers = config.GetCVar(CCVars.GameRoleTimers);

        await server.WaitAssertion(() =>
        {
            config.SetCVar(CCVars.GameRoleTimers, true);
            var requirements = entities.System<CharacterRequirementsSystem>();
            var job = prototypes.Index<JobPrototype>("InstituteCourser");
            var noHours = new Dictionary<string, TimeSpan>();
            var courser = HumanoidCharacterProfile.DefaultWithSpecies("Courser");
            var human = HumanoidCharacterProfile.DefaultWithSpecies("Human");

            bool Check(HumanoidCharacterProfile profile, bool jobWhitelisted) =>
                requirements.CheckRequirementsValid(job.Requirements!, job, profile, noHours, true, job,
                    entities, prototypes, config, sponsors, out _, jobWhitelisted: jobWhitelisted);

            Assert.That(Check(courser, jobWhitelisted: false), Is.False, "a Courser with no hours got in without the whitelist");
            Assert.That(Check(courser, jobWhitelisted: true), Is.True, "the whitelist didn't skip the hour requirement");
            Assert.That(Check(human, jobWhitelisted: true), Is.False, "the whitelist let a non-Courser race in");
        });

        await server.WaitPost(() => config.SetCVar(CCVars.GameRoleTimers, oldTimers));
        await pair.CleanReturnAsync();
    }
}
