using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._NC.Sponsor;
using Content.Shared.CCVar;
using Content.Shared.Customization.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class RailroadProgressionTests
{
    [TestCase(1, "Worker", 20)]
    [TestCase(2, "Worker", 20)]
    [TestCase(1, "Runner", 40)]
    [TestCase(2, "Runner", 40)]
    [TestCase(1, "Agent", 60)]
    [TestCase(2, "Agent", 60)]
    [TestCase(1, "Conductor", 80)]
    [TestCase(2, "Conductor", 80)]
    [TestCase(1, "SafehouseOwner", 100)]
    [TestCase(2, "SafehouseOwner", 100)]
    public async Task RailroadUnlocksUseCombinedTimeAndCannotBeSkippedByWhitelist(int house, string rank, int hours)
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var config = pair.Server.ResolveDependency<IConfigurationManager>();
            var sponsor = pair.Server.ResolveDependency<ISharedSponsorManager>();
            var previous = config.GetCVar(CCVars.GameRoleTimers);
            config.SetCVar(CCVars.GameRoleTimers, true);
            try
            {
                var job = prototypes.Index<JobPrototype>($"RailroadSafehouse{house}{rank}");
                var requirement = job.Requirements!.OfType<CharacterDepartmentTimeRequirement>().Single();
                Assert.That(requirement.Min, Is.EqualTo(TimeSpan.FromHours(hours)));
                Assert.That(requirement.CanBeBypassedByJobWhitelist(job), Is.False);
                Assert.That(job.Whitelisted, Is.EqualTo(rank is "Agent" or "Conductor" or "SafehouseOwner"));
                var times = new Dictionary<string, TimeSpan>
                {
                    ["RailroadSafehouse1Intern"] = TimeSpan.FromHours(hours / 2d),
                    ["RailroadSafehouse2Worker"] = TimeSpan.FromHours(hours / 2d) - TimeSpan.FromSeconds(1),
                    ["MinutemenGeneral"] = TimeSpan.FromHours(1000)
                };
                var system = em.System<CharacterRequirementsSystem>();
                bool Eligible() => system.CheckRequirementValid(requirement, job,
                    HumanoidCharacterProfile.DefaultWithSpecies(), times, true, job, em,
                    prototypes, config, sponsor, out _, jobWhitelisted: true);
                Assert.That(Eligible(), Is.False, "One second below the threshold, even with a job whitelist and unrelated time.");
                times["RailroadSafehouse2Worker"] += TimeSpan.FromSeconds(1);
                Assert.That(Eligible(), Is.True, "Time from both safehouses combines at the exact threshold.");
                requirement.AdditionalDepartments.Add("RailroadSafehouse1");
                try
                {
                    times["RailroadSafehouse2Worker"] -= TimeSpan.FromSeconds(1);
                    Assert.That(Eligible(), Is.False, "Duplicate departments do not count time twice.");
                }
                finally { requirement.AdditionalDepartments.RemoveAt(requirement.AdditionalDepartments.Count - 1); }
            }
            finally { config.SetCVar(CCVars.GameRoleTimers, previous); }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RetiredJobTimersWorkWithoutDepartmentMembershipOnServerAndClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var config = pair.Server.ResolveDependency<IConfigurationManager>();
        var old = config.GetCVar(CCVars.GameRoleTimers);
        await pair.Server.WaitPost(() => config.SetCVar(CCVars.GameRoleTimers, true));
        await pair.RunTicksSync(15);
        try
        {
            void Check(IEntityManager em, IPrototypeManager prototypes, IConfigurationManager cfg, ISharedSponsorManager sponsor)
            {
                Assert.That(cfg.GetCVar(CCVars.GameRoleTimers), Is.True);
                var job = prototypes.Index<JobPrototype>("RailroadSafehouse1Worker");
                var requirement = new CharacterPlaytimeRequirement { Tracker = "NCRSoldier", Min = TimeSpan.FromHours(20) };
                var times = new Dictionary<string, TimeSpan>();
                var system = em.System<CharacterRequirementsSystem>();
                bool Eligible(out string? reason) => system.CheckRequirementValid(requirement, job,
                    HumanoidCharacterProfile.DefaultWithSpecies(), times, false, job, em,
                    prototypes, cfg, sponsor, out reason);
                Assert.That(Eligible(out var reason), Is.False);
                Assert.That(reason, Is.Not.Null.And.Not.Empty, "Locked timer still explains its requirement.");
                times["NCRSoldier"] = TimeSpan.FromHours(20);
                Assert.That(Eligible(out _), Is.True, "Historical playtime still satisfies the requirement.");
            }
            await pair.Server.WaitAssertion(() => Check(pair.Server.ResolveDependency<IEntityManager>(),
                pair.Server.ResolveDependency<IPrototypeManager>(), config, pair.Server.ResolveDependency<ISharedSponsorManager>()));
            await pair.Client.WaitAssertion(() => Check(pair.Client.ResolveDependency<IEntityManager>(),
                pair.Client.ResolveDependency<IPrototypeManager>(), pair.Client.ResolveDependency<IConfigurationManager>(),
                pair.Client.ResolveDependency<ISharedSponsorManager>()));
            await pair.Client.WaitAssertion(() =>
            {
                var ui = pair.Client.ResolveDependency<Robust.Client.UserInterface.IUserInterfaceManager>();
                var controller = ui.GetUIController<Content.Client.Lobby.LobbyUIController>();
                var openEditor = typeof(Content.Client.Lobby.LobbyUIController).GetMethod("EnsureGui",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Assert.DoesNotThrow(() => openEditor.Invoke(controller, null),
                    "Opening character setup with timers enabled must not crash on retired-job loadouts.");
            });
        }
        finally
        {
            await pair.Server.WaitPost(() => config.SetCVar(CCVars.GameRoleTimers, old));
            await pair.RunTicksSync(15);
        }
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EntryRolesAndLeadershipKeepTheirExistingRestrictionsAndMinutemenHaveDividers()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var id in new[] { "RailroadSafehouse1Intern", "RailroadSafehouse2Intern" })
            {
                var job = prototypes.Index<JobPrototype>(id);
                Assert.That(job.Whitelisted, Is.False);
                Assert.That(job.Requirements, Is.Null.Or.Empty);
            }
            foreach (var id in new[] { "RailroadAlphaLeader", "RailroadInspector" })
            {
                var job = prototypes.Index<JobPrototype>(id);
                Assert.That(job.Whitelisted, Is.True);
                Assert.That(job.HideWithoutJobWhitelist, Is.True);
                Assert.That(job.Requirements, Is.Null.Or.Empty);
            }
            foreach (var id in prototypes.Index<DepartmentPrototype>("Minutemen").Roles)
                Assert.That(prototypes.Index(id).ShowBorderBelow, Is.True, id.Id);
            Assert.That(prototypes.Index<JobPrototype>("MinutemenGeneral").ShowBorder, Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
