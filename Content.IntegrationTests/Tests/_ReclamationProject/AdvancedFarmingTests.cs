using System.Linq;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Medical.CPR;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class AdvancedFarmingTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task BookTeachesOnceWithoutRequiringOrGrantingCpr(bool knowsCpr)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid usedBook = default;
        EntityUid spareBook = default;
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            if (knowsCpr) em.AddComponent<CPRTrainingComponent>(user);
            usedBook = em.SpawnEntity("ReclamationTrainingManualAdvancedFarming", map.GridCoords);
            spareBook = em.SpawnEntity("ReclamationTrainingManualAdvancedFarming", map.GridCoords);
            Assert.That(em.HasComponent<AdvancedFarmingComponent>(user), Is.False);
            em.EventBus.RaiseLocalEvent(usedBook, new UseInHandEvent(user));
            Assert.That(em.HasComponent<AdvancedFarmingComponent>(user), Is.True);
            Assert.That(em.HasComponent<CPRTrainingComponent>(user), Is.EqualTo(knowsCpr));
            var useAgain = new UseInHandEvent(user);
            em.EventBus.RaiseLocalEvent(spareBook, useAgain);
            Assert.That(useAgain.Handled, Is.False, "Already-known books must not be consumed.");
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(usedBook), Is.False);
            Assert.That(em.EntityExists(spareBook), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("N14Soil")]
    [TestCase("N14FloraWildAgaveClippingSeed")]
    [TestCase("N14FloraProduceWildAgave")]
    public async Task AnalysisIsRestrictedToTrainedAccessibleCharacters(string targetPrototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var target = em.SpawnEntity(targetPrototype, map.GridCoords);
            Assert.That(HasAnalysis(true), Is.False);
            var book = em.SpawnEntity("ReclamationTrainingManualAdvancedFarming", map.GridCoords);
            em.EventBus.RaiseLocalEvent(book, new UseInHandEvent(user));
            Assert.That(HasAnalysis(true), Is.True);
            Assert.That(HasAnalysis(false), Is.False, "Knowledge does not bypass access restrictions.");

            bool HasAnalysis(bool accessible)
            {
                var verbs = new GetVerbsEvent<ExamineVerb>(user, target, null, null, true, true, accessible, new());
                em.EventBus.RaiseLocalEvent(target, verbs, true);
                return verbs.Verbs.Any(verb => verb.Text == "Plant Analysis");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReportReadsLivePlantConditionAndSeedChemistry()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var botany = em.System<BotanySystem>();
            var clipping = em.SpawnEntity("N14FloraWildAgaveClippingSeed", map.GridCoords);
            Assert.That(botany.TryGetSeed(em.GetComponent<SeedComponent>(clipping), out var seed), Is.True);
            var holder = new PlantHolderComponent { Seed = seed, Health = 37, WaterLevel = 12, MutationLevel = 4, MutationMod = 2 };
            var formatted = botany.DescribeFarmingPlant(seed, holder);
            Assert.That(formatted.ToMarkup(), Does.Contain("[bold]Plant Analysis[/bold]"));
            var report = formatted.ToString();
            Assert.That(report, Does.Not.Contain("boldPlant"));
            Assert.That(report, Does.Contain("37 / 100"));
            Assert.That(report, Does.Contain("Pending mutation strength: 4"));
            Assert.That(report, Does.Contain("Mutation multiplier: 2"));
            Assert.That(report, Does.Contain("Crop chemistry"));
            holder.Health = 61;
            holder.Dead = true;
            report = botany.DescribeFarmingPlant(seed, holder).ToString();
            Assert.That(report, Does.Contain("61 / 100"));
            Assert.That(report, Does.Contain("Status: Dead"));
            Assert.That(botany.DescribeFarmingPlant(null).ToString(), Does.Contain("Nothing planted"));
        });
        await pair.CleanReturnAsync();
    }
}
