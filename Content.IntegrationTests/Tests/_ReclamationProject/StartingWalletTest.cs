#nullable enable annotations

using System.Linq;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class StartingWalletTest
{
    [Test]
    public async Task EveryJobStartingKitIncludesAWallet()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var job in prototypes.EnumeratePrototypes<JobPrototype>())
            {
                if (job.StartingGear is not { } gearId)
                    continue;

                var wallet = prototypes.Index(gearId).GetGear("wallet", null);
                Assert.That(wallet, Is.Not.Empty, job.ID);
                Assert.That(prototypes.EnumerateParents<EntityPrototype>(wallet, includeSelf: true)
                    .Any(parent => parent.ID == "N14Wallet"), Is.True, job.ID);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryReclamationMapJobSpawnsWithAWallet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var spawning = em.System<StationSpawningSystem>();
            var inventory = em.System<InventorySystem>();
            var jobs = new[] { "ReclamationWendover", "ReclamationMercerIsland", "CommonWealth" }
                .SelectMany(id => prototypes.Index<GameMapPrototype>(id).Stations.Values)
                .SelectMany(station => station.StationComponentOverrides.Values)
                .Select(entry => entry.Component).OfType<StationJobsComponent>()
                .SelectMany(station => station.SetupAvailableJobs.Keys).Distinct().ToArray();
            Assert.That(jobs, Is.Not.Empty);
            foreach (var job in jobs)
            {
                var mob = spawning.SpawnPlayerMob(map.GridCoords, new JobComponent { Prototype = job },
                    HumanoidCharacterProfile.DefaultWithSpecies("Human"), null);
                Assert.That(inventory.TryGetSlotEntity(mob, "wallet", out _), Is.True, job);
                em.DeleteEntity(mob);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("PackCub", "Human")]
    [TestCase("ForgedBranded", "Human")]
    [TestCase("SuperMutantEnclave", "SuperMutant")]
    [TestCase("SyntheticMrHandy", "RobotMrHandy")]
    [TestCase("DiamondCityTakahashi", "Human")]
    [TestCase("SentientDeathclaw", "BwonsamdiDeathclaw")]
    [TestCase("SentientDeathclawRegular", "DeathclawHatchling")]
    [TestCase("Visitor", "Human")]
    [TestCase("DiamondCityShopkeeper", "Human", "N14WalletCash", "N14CurrencyPrewarMoney50")]
    [TestCase("DiamondCityMayor", "Human", "N14WalletLottaCash", "N14CurrencyPrewarMoney100")]
    public async Task JobSpawnsWithItsWallet(string job, string species, string expected = "N14Wallet", string? cash = null)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var mob = em.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords,
                new JobComponent { Prototype = job }, HumanoidCharacterProfile.DefaultWithSpecies(species), null);
            Assert.That(em.System<InventorySystem>().TryGetSlotEntity(mob, "wallet", out var wallet), Is.True, job);
            Assert.That(em.GetComponent<MetaDataComponent>(wallet!.Value).EntityPrototype?.ID, Is.EqualTo(expected));
            var contents = em.GetComponent<StorageComponent>(wallet.Value).Container.ContainedEntities;
            var ids = contents.Select(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID);
            Assert.That(ids, Is.EqualTo(cash == null ? System.Array.Empty<string>() : new[] { cash }));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("MobHumanPackCub")]
    [TestCase("N14MobPetEyebotMBoS")]
    [TestCase("MisfitsMobPetEyebotOSI")]
    [TestCase("MisfitsMobPetEyebotEnclave")]
    public async Task PreEquippedBodiesCarryTheirWallet(string body)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var mob = em.SpawnEntity(body, map.GridCoords);
            Assert.That(em.System<InventorySystem>().TryGetSlotEntity(mob, "wallet", out var wallet), Is.True, body);
            Assert.That(em.GetComponent<MetaDataComponent>(wallet!.Value).EntityPrototype?.ID, Is.EqualTo("N14Wallet"));
        });
        await pair.CleanReturnAsync();
    }
}
