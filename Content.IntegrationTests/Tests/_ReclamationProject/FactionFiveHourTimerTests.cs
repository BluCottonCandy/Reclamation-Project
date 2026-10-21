using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._NC.Sponsor;
using Content.Shared.CCVar;
using Content.Shared.Customization.Systems;
using Content.Shared.Preferences;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class FactionFiveHourTimerTests
{
    [TestCase("MinutemenPrivate", "MinutemenRecruit")]
    [TestCase("MinutemenCorporal", "MinutemenPrivate")]
    [TestCase("MinutemenSergeant", "MinutemenCorporal")]
    [TestCase("MinutemenLieutenant", "MinutemenSergeant")]
    [TestCase("MinutemenCaptain", "MinutemenLieutenant")]
    [TestCase("MinutemenCommander", "MinutemenCaptain")]
    [TestCase("MinutemenColonel", "MinutemenCommander")]
    [TestCase("MinutemenGeneral", "MinutemenColonel")]
    [TestCase("BoSMidSquire", "BoSInitiate")]
    [TestCase("BoSKnight", "BoSMidSquire")]
    [TestCase("BoSScribe", "BoSMidSquire")]
    [TestCase("BoSLancerKnight", "BoSMidSquire")]
    [TestCase("BoSPaladin", "BoSKnight")]
    [TestCase("BoSMidKnight", "BoSKnight")]
    [TestCase("BoSMidScribe", "BoSScribe")]
    [TestCase("BoSMidPaladin", "BoSPaladin")]
    [TestCase("BoSHeadKnight", "BoSMidKnight")]
    [TestCase("BoSWestScribe", "BoSMidScribe")]
    [TestCase("BoSMidPaladinCommander", "BoSMidPaladin")]
    [TestCase("BoSWestElderCommander", "BoSHeadKnight")]
    [TestCase("BoSWestElderCommander", "BoSWestScribe")]
    [TestCase("BoSWestElderCommander", "BoSMidPaladinCommander")]
    [TestCase("RailroadSafehouse1Worker", "RailroadSafehouse1Intern")]
    [TestCase("RailroadSafehouse1Worker", "RailroadSafehouse2Intern")]
    [TestCase("RailroadSafehouse2Worker", "RailroadSafehouse1Intern")]
    [TestCase("RailroadSafehouse2Worker", "RailroadSafehouse2Intern")]
    [TestCase("RailroadSafehouse1Runner", "RailroadSafehouse1Worker")]
    [TestCase("RailroadSafehouse1Runner", "RailroadSafehouse2Worker")]
    [TestCase("RailroadSafehouse2Runner", "RailroadSafehouse1Worker")]
    [TestCase("RailroadSafehouse2Runner", "RailroadSafehouse2Worker")]
    [TestCase("RailroadSafehouse1Agent", "RailroadSafehouse1Runner")]
    [TestCase("RailroadSafehouse1Agent", "RailroadSafehouse2Runner")]
    [TestCase("RailroadSafehouse2Agent", "RailroadSafehouse1Runner")]
    [TestCase("RailroadSafehouse2Agent", "RailroadSafehouse2Runner")]
    [TestCase("RailroadSafehouse1Conductor", "RailroadSafehouse1Agent")]
    [TestCase("RailroadSafehouse1Conductor", "RailroadSafehouse2Agent")]
    [TestCase("RailroadSafehouse2Conductor", "RailroadSafehouse1Agent")]
    [TestCase("RailroadSafehouse2Conductor", "RailroadSafehouse2Agent")]
    [TestCase("RailroadSafehouse1SafehouseOwner", "RailroadSafehouse1Conductor")]
    [TestCase("RailroadSafehouse1SafehouseOwner", "RailroadSafehouse2Conductor")]
    [TestCase("RailroadSafehouse2SafehouseOwner", "RailroadSafehouse1Conductor")]
    [TestCase("RailroadSafehouse2SafehouseOwner", "RailroadSafehouse2Conductor")]
    public async Task OnlyThePrecedingRoleUnlocksPromotion(string id, string tracker)
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var config = pair.Server.ResolveDependency<IConfigurationManager>();
            var sponsor = pair.Server.ResolveDependency<ISharedSponsorManager>();
            var old = config.GetCVar(CCVars.GameRoleTimers);
            config.SetCVar(CCVars.GameRoleTimers, true);
            try
            {
                var job = prototypes.Index<JobPrototype>(id);
                Assert.That(job.JobWhitelistBypassesRoleTimers, Is.True);
                var times = prototypes.EnumeratePrototypes<PlayTimeTrackerPrototype>()
                    .ToDictionary(t => t.ID, _ => TimeSpan.FromHours(1000));
                var timers = job.Requirements!.Where(r => r is CharacterPlaytimeRequirement or CharacterLogicOrRequirement).ToArray();
                Assert.That(timers, Has.Length.EqualTo(1));
                Assert.That(job.Requirements.OfType<CharacterDepartmentTimeRequirement>(), Is.Empty);
                var timer = timers.Single();
                var children = timer is CharacterLogicOrRequirement choice
                    ? choice.Requirements.Cast<CharacterPlaytimeRequirement>().ToArray()
                    : new[] { (CharacterPlaytimeRequirement) timer };
                foreach (var child in children)
                {
                    Assert.That(child.Min, Is.EqualTo(TimeSpan.FromHours(5)));
                    times[child.Tracker] = TimeSpan.Zero;
                    foreach (var extra in child.AdditionalTrackers)
                        times[extra] = TimeSpan.Zero;
                }
                bool Eligible(bool approved = false) => em.System<CharacterRequirementsSystem>().CheckRequirementsValid(job.Requirements,
                    job, HumanoidCharacterProfile.DefaultWithSpecies(), times, true, job,
                    em, prototypes, config, sponsor, out _, jobWhitelisted: approved);
                Assert.That(Eligible(), Is.False, "Even enormous time in every other role cannot skip the preceding rank.");
                Assert.That(Eligible(true), Is.True, "Job whitelist approval bypasses the playtime requirement.");
                times[tracker] = TimeSpan.FromHours(5) - TimeSpan.FromSeconds(1);
                Assert.That(Eligible(), Is.False, "One second below the preceding-role threshold remains locked.");
                times[tracker] += TimeSpan.FromSeconds(1);
                Assert.That(Eligible(), Is.True, "Five hours in the preceding role unlocks the promotion.");
                if (id.StartsWith("Railroad"))
                {
                    var combined = (CharacterPlaytimeRequirement) timer;
                    Assert.That(combined.AdditionalTrackers, Has.Count.EqualTo(1));
                    times[combined.Tracker] = TimeSpan.FromMinutes(150);
                    times[combined.AdditionalTrackers.Single()] = TimeSpan.FromMinutes(150) - TimeSpan.FromSeconds(1);
                    Assert.That(Eligible(), Is.False, "Split time one second below 300 minutes stays locked.");
                    times[combined.AdditionalTrackers.Single()] += TimeSpan.FromSeconds(1);
                    Assert.That(Eligible(), Is.True, "150 minutes in each safehouse unlocks either destination.");
                    combined.AdditionalTrackers.Add(combined.Tracker);
                    try
                    {
                        times[combined.Tracker] -= TimeSpan.FromSeconds(1);
                        Assert.That(Eligible(), Is.False, "Duplicate trackers must not double count time.");
                    }
                    finally { combined.AdditionalTrackers.RemoveAt(combined.AdditionalTrackers.Count - 1); }
                }
            }
            finally { config.SetCVar(CCVars.GameRoleTimers, old); }
        });
        await pair.CleanReturnAsync();
    }
}
