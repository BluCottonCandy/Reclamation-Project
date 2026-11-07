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
public sealed class FactionTimeAlternativesTests
{
    [TestCase("ForgedBranded", 300, "Forged")]
    [TestCase("ForgedEnforcer", 600, "Forged")]
    [TestCase("ForgedVeteran", 900, "Forged")]
    [TestCase("Forgemaster", 1500, "Forged")]
    [TestCase("MinutemenPrivate", 300, "Minutemen")]
    [TestCase("MinutemenCorporal", 600, "Minutemen")]
    [TestCase("MinutemenSergeant", 1500, "Minutemen")]
    [TestCase("MinutemenHeavyTrooper", 1500, "Minutemen")]
    [TestCase("MinutemenLieutenant", 1500, "Minutemen")]
    [TestCase("MinutemenCaptain", 1500, "Minutemen")]
    [TestCase("MinutemenCommander", 1500, "Minutemen")]
    [TestCase("MinutemenColonel", 1500, "Minutemen")]
    [TestCase("MinutemenGeneral", 1500, "Minutemen")]
    [TestCase("BoSMidSquire", 300, "BrotherhoodOfSteel")]
    [TestCase("BoSKnight", 600, "BrotherhoodOfSteel")]
    [TestCase("BoSScribe", 600, "BrotherhoodOfSteel")]
    [TestCase("BoSLancerKnight", 600, "BrotherhoodOfSteel")]
    [TestCase("BoSPaladin", 900, "BrotherhoodOfSteel")]
    [TestCase("BoSMidKnight", 1500, "BrotherhoodOfSteel")]
    [TestCase("BoSMidScribe", 1500, "BrotherhoodOfSteel")]
    [TestCase("BoSMidPaladin", 1500, "BrotherhoodOfSteel")]
    [TestCase("BoSHeadKnight", 1500, "BrotherhoodOfSteel")]
    [TestCase("BoSWestScribe", 1500, "BrotherhoodOfSteel")]
    [TestCase("BoSMidPaladinCommander", 1500, "BrotherhoodOfSteel")]
    [TestCase("BoSWestElderCommander", 1500, "BrotherhoodOfSteel")]
    [TestCase("InstituteCourser", 1500, "Institute")]
    [TestCase("InstituteDivisionHead", 1500, "Institute")]
    [TestCase("InstituteDirector", 1500, "Institute")]
    [TestCase("RailroadSafehouse1Worker", 300, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse1Runner", 600, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse1Agent", 1500, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse1Conductor", 1500, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse1SafehouseOwner", 1500, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse2Worker", 300, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse2Runner", 600, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse2Agent", 1500, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse2Conductor", 1500, "RailroadSafehouse1")]
    [TestCase("RailroadSafehouse2SafehouseOwner", 1500, "RailroadSafehouse1")]
    [TestCase("TriggermenSoldato", 300, "Triggermen")]
    [TestCase("TriggermenEnforcer", 600, "Triggermen")]
    [TestCase("TriggermenSotocapo", 1500, "Triggermen")]
    [TestCase("TriggermenConsigliere", 1500, "Triggermen")]
    [TestCase("TriggermenBoss", 1500, "Triggermen")]
    public async System.Threading.Tasks.Task FactionTimeOrPreviousRole(string id, int minutes, string department)
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var config = pair.Server.ResolveDependency<IConfigurationManager>();
            var old = config.GetCVar(CCVars.GameRoleTimers);
            config.SetCVar(CCVars.GameRoleTimers, true);
            try { Verify(pair.Server.ResolveDependency<IEntityManager>(), pair.Server.ResolveDependency<IPrototypeManager>(), config,
                pair.Server.ResolveDependency<ISharedSponsorManager>(), id, minutes, department); }
            finally { config.SetCVar(CCVars.GameRoleTimers, old); }
        });
        await pair.CleanReturnAsync();
    }
    public static void Verify(IEntityManager em, IPrototypeManager prototypes, IConfigurationManager config, ISharedSponsorManager sponsor, string id, int minutes, string department)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(id + ": " + message); }
        var job = prototypes.Index<JobPrototype>(id);
        var choice = job.Requirements!.OfType<CharacterLogicOrRequirement>().Single();
        var alternative = choice.Requirements.OfType<CharacterDepartmentTimeRequirement>().Single();
        Check(alternative.Department == department && alternative.Min == TimeSpan.FromMinutes(minutes), "wrong faction threshold");
        Check(!job.JobWhitelistBypassesRoleTimers, "whitelist must not waive timers");
        var times = new Dictionary<string, TimeSpan>();
        times[PlayTimeTrackingShared.TrackerOverall] = TimeSpan.FromHours(10000);
        times["Wastelander"] = TimeSpan.FromHours(10000);
        bool Eligible() => em.System<CharacterRequirementsSystem>().CheckRequirementsValid(new List<CharacterRequirement> { choice },
            job, HumanoidCharacterProfile.DefaultWithSpecies(), times, true, job, em, prototypes, config, sponsor, out _);
        Check(!Eligible(), "unrelated faction/server-wide time unlocked rank");
        var previous = choice.Requirements.First();
        IEnumerable<CharacterPlaytimeRequirement> Previous(CharacterRequirement req) => req is CharacterPlaytimeRequirement timer
            ? new[] { timer } : ((CharacterLogicOrRequirement) req).Requirements.SelectMany(Previous);
        var previousTimers = Previous(previous).ToArray();
        var precedingTrackers = previousTimers.SelectMany(x => x.AdditionalTrackers.Select(t => t.Id).Append(x.Tracker.Id)).ToHashSet();
        var departments = alternative.AdditionalDepartments.Prepend(alternative.Department).Select(d => prototypes.Index(d));
        var factionTrackers = departments.SelectMany(d => d.Roles).Select(j => prototypes.Index(j).PlayTimeTracker.Id).Distinct().ToArray();
        var sample = factionTrackers.Where(t => !precedingTrackers.Contains(t)).Take(2).ToArray();
        Check(sample.Length == 2, "need independent faction roles for boundary check");
        times[sample[0]] = TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(1);
        Check(!Eligible(), "one second below faction threshold unlocked rank");
        times[sample[0]] += TimeSpan.FromSeconds(1);
        Check(Eligible(), "exact faction threshold stayed locked");
        times[sample[0]] = TimeSpan.FromMinutes(minutes / 2.0);
        times[sample[1]] = TimeSpan.FromMinutes(minutes / 2.0);
        Check(Eligible(), "time across faction roles was not combined");
        times[sample[1]] -= TimeSpan.FromSeconds(1);
        alternative.AdditionalDepartments.Add(alternative.Department);
        try { Check(!Eligible(), "duplicate department double-counted faction time"); }
        finally { alternative.AdditionalDepartments.RemoveAt(alternative.AdditionalDepartments.Count - 1); }
        times.Clear();
        var legacy = previousTimers.First();
        times[legacy.Tracker] = legacy.Min;
        Check(Eligible(), "existing previous-role route no longer works");
    }
}
