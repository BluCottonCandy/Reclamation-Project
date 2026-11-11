using System.Linq;
using Content.Server._ReclamationProject.DiamondCity;
using Content.Server.Speech;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Content.Shared._Misfits.Currency.Components;
using Content.Shared._ReclamationProject.DiamondCity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - Diamond City: Takahashi's noodle-cup power, his one line, and the job wiring.
/// </summary>
[TestFixture]
public sealed class DiamondCityTakahashiTest
{
    private static readonly string[] DiamondCityJobs =
    {
        "DiamondCityMayor", "DiamondCitySecurityChief", "DiamondCitySecuritySergeant",
        "DiamondCitySecurityGuard", "DiamondCitySecurityRecruit", "DiamondCityMedic",
        "DiamondCityShopkeeper", "DiamondCityTakahashi", "DiamondCityTownsfolk",
    };

    [Test]
    public async Task NoodleCupCostsTwoCaps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var hands = em.System<SharedHandsSystem>();
            var maker = em.System<NoodleCupMakerSystem>();

            bool Press(EntityUid uid)
            {
                var ev = new MakeNoodleCupActionEvent { Performer = uid };
                em.EventBus.RaiseLocalEvent(uid, ev);
                return ev.Handled;
            }

            int CupsHeld(EntityUid uid) => hands.EnumerateHeld(uid)
                .Count(e => em.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "RPFoodNoodleCup");

            // Wallet pays first: 3 caps -> 1 cup, 1 cap left.
            var robot = em.SpawnEntity("N14PlayerSyntheticProtectron", coords);
            em.EnsureComponent<NoodleCupMakerComponent>(robot);
            var wallet = em.EnsureComponent<PersistentCurrencyComponent>(robot);
            wallet.Bottlecaps = 3;
            Assert.That(em.GetComponent<NoodleCupMakerComponent>(robot).Action, Is.Not.Null, "power button added");
            Assert.That(Press(robot), Is.True, "made a cup from the wallet");
            Assert.That(wallet.Bottlecaps, Is.EqualTo(1), "wallet charged 2 caps");
            Assert.That(CupsHeld(robot), Is.EqualTo(1), "cup is in hand");

            // 1 cap is not enough: nothing made, nothing charged.
            Assert.That(Press(robot), Is.False, "can't pay");
            Assert.That(wallet.Bottlecaps, Is.EqualTo(1), "short purse charges nothing");

            // Caps held in hand pay when the wallet is short: stack of 5 -> 3 left.
            var human = em.SpawnEntity("MobHuman", coords);
            em.EnsureComponent<NoodleCupMakerComponent>(human);
            var caps = em.SpawnEntity("N14CurrencyCap", coords);
            em.System<SharedStackSystem>().SetCount(caps, 5);
            Assert.That(hands.TryPickupAnyHand(human, caps), Is.True);
            Assert.That(maker.TryPay(human, 2), Is.True, "paid from held caps");
            Assert.That(em.GetComponent<StackComponent>(caps).Count, Is.EqualTo(3), "2 caps taken from the stack");
            Assert.That(maker.TryPay(human, 4), Is.False, "3 held caps can't pay 4");
            Assert.That(em.GetComponent<StackComponent>(caps).Count, Is.EqualTo(3), "failed payment takes nothing");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TakahashiOnlySaysHisLine()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var robot = em.SpawnEntity("N14PlayerSyntheticProtectron", new EntityCoordinates(map, System.Numerics.Vector2.Zero));
            var accent = em.EnsureComponent<Content.Server.Speech.Components.ReplacementAccentComponent>(robot);
            accent.Accent = "takahashi";

            var ev = new AccentGetEvent(robot, "Hello there, what are you doing?");
            em.EventBus.RaiseLocalEvent(robot, ev);
            Assert.That(ev.Message, Is.EqualTo("Nan-ni shimasho-ka?"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HumanCharacterSpawnsAsTakahashiProtectron()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Kenji Test");
            var job = new JobComponent { Prototype = "DiamondCityTakahashi" };

            var mob = em.System<StationSpawningSystem>().SpawnPlayerMob(coords, job, profile, null);

            Assert.That(em.GetComponent<MetaDataComponent>(mob).EntityPrototype?.ID, Is.EqualTo("N14PlayerSyntheticProtectron"), "spawned as a Protectron");
            Assert.That(em.GetComponent<MetaDataComponent>(mob).EntityName, Is.EqualTo("Kenji Test"), "keeps the character's name");
            Assert.That(em.HasComponent<NoodleCupMakerComponent>(mob), Is.True, "has the noodle power");
            Assert.That(em.GetComponent<NoodleCupMakerComponent>(mob).Action, Is.Not.Null, "power button added");
            Assert.That(em.GetComponent<Content.Server.Speech.Components.ReplacementAccentComponent>(mob).Accent, Is.EqualTo("takahashi"));
            Assert.That(em.GetComponent<AccessComponent>(mob).Tags.Any(t => t == "DiamondCity"), Is.True, "Diamond City door access");
            Assert.That(em.System<SharedHandsSystem>().EnumerateHeld(mob)
                .Any(e => em.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "RPBoxTakahashiNoodles"), Is.True, "holding the noodle box");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DiamondCityJobsAreWired()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var department = protos.Index<DepartmentPrototype>("DiamondCity");
            foreach (var job in DiamondCityJobs)
            {
                Assert.That(protos.HasIndex<JobPrototype>(job), Is.True, job);
                Assert.That(department.Roles.Contains(job), Is.True, $"{job} in the Diamond City tab");
            }

            var takahashi = protos.Index<JobPrototype>("DiamondCityTakahashi");
            Assert.That(takahashi.JobEntity?.Id, Is.EqualTo("N14PlayerSyntheticProtectron"), "always a Protectron body");
            Assert.That(takahashi.Requirements, Is.Null.Or.Empty, "anyone can take Takahashi");

            foreach (var species in new[] { "RobotProtectron", "RobotProtectronPolice", "RobotProtectronBuilder", "RobotProtectronFire" })
            {
                var proto = protos.Index<SpeciesPrototype>(species);
                Assert.That(proto.RestrictedJobs.Any(j => j == "DiamondCityTakahashi"), Is.True, $"{species} can take Takahashi");
            }

            Assert.That(protos.HasIndex<EntityPrototype>("DiamondCitySpawnPointTakahashi"), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
