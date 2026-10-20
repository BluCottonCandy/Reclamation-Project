using System.Collections.Generic;
using System.Linq;
using Content.Shared.Nutrition.Components;
using Content.Shared.Storage;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Content.Client._ReclamationProject.Cookbook;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Components;
using Content.Client.Guidebook.Richtext;
using Content.Shared._ReclamationProject.Cookbook;
using Content.Shared.Interaction.Events;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class CookbookTests
{
    [Test]
    public async Task SuperMutantVariantsYieldOneOrTwoRecipeCompatibleRawMeat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var variants = prototypes.EnumeratePrototypes<EntityPrototype>().Where(prototype => !prototype.Abstract &&
                prototypes.EnumerateAllParents<EntityPrototype>(prototype.ID).Any(parent => parent.id == "N14MobBaseHostileSuperMutant")).ToArray();
            Assert.That(variants.Select(prototype => prototype.ID), Does.Contain("N14MobBehemoth"));
            Assert.That(variants.Select(prototype => prototype.ID), Does.Contain("N14MobNightkinVeteran"));
            var random = new RobustRandom();
            random.SetSeed(42);
            foreach (var prototype in variants)
            {
                Assert.That(prototype.Components.TryGetValue("Butcherable", out var component), Is.True, prototype.ID);
                var butcher = (ButcherableComponent) component!.Component;
                Assert.That(butcher.Type, Is.EqualTo(ButcheringType.Knife), prototype.ID);
                var counts = new HashSet<int>();
                for (var roll = 0; roll < 100; roll++)
                {
                    var drops = EntitySpawnCollection.GetSpawns(butcher.SpawnedEntities, random);
                    Assert.That(drops.Count, Is.InRange(1, 2), prototype.ID);
                    Assert.That(drops.All(id => id == "N14FoodMeatSupermutant"), Is.True, prototype.ID);
                    counts.Add(drops.Count);
                }
                Assert.That(counts, Is.EquivalentTo(new[] { 1, 2 }), prototype.ID);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StandaloneCookbookPreservesAllCopiedRecipesAndOpensItsOwnWindow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid serverBook = default;
        NetEntity netBook = default;
        await pair.Server.WaitAssertion(() =>
        {
            serverBook = server.SpawnEntity("ReclamationCookbook", map.GridCoords);
            netBook = server.GetNetEntity(serverBook);
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var entities = pair.Client.ResolveDependency<IEntityManager>();
            var resources = pair.Client.ResolveDependency<IResourceManager>();
            var parser = pair.Client.ResolveDependency<DocumentParsingManager>();
            var book = entities.GetEntity(netBook);
            var component = entities.GetComponent<CookbookComponent>(book);
            Assert.That(entities.HasComponent<GuideHelpComponent>(book), Is.False);
            Assert.That(component.Chapters.Count, Is.EqualTo(9));
            var recipes = new List<string>();
            foreach (var chapter in component.Chapters.Values.OrderBy(chapter => chapter.Order))
            {
                Assert.That(chapter.Text.ToString(), Does.StartWith("/ServerInfo/_ReclamationProject/Cookbook/"));
                using var reader = resources.ContentFileReadText(chapter.Text);
                var text = reader.ReadToEnd();
                using var document = new Document();
                Assert.That(parser.TryAddMarkup(document, text), Is.True, chapter.Text.ToString());
                foreach (Match match in Regex.Matches(text,
                             @"## ([^\r\n]+)\s+\[bold\]Ingredients:\[/bold\] ([^\r\n]+)\s+\[bold\]Cook:\[/bold\] ([^\r\n]+)"))
                    recipes.Add(string.Join("|", match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value));
            }
            Assert.That(recipes.Count, Is.EqualTo(174));
            // Snapshot of the copied recipes, deliberately independent of future guidebook changes.
            Assert.That(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", recipes)))),
                Is.EqualTo("D50660147C4239497736DD7228EE6F03A3BB66DA26D6E54E5E8B0C6C277C22C9"));

            var use = new UseInHandEvent(book);
            entities.EventBus.RaiseLocalEvent(book, use);
            Assert.That(use.Handled, Is.True);
            var system = entities.System<CookbookSystem>();
            Assert.That(system.Window, Is.Not.Null);
            Assert.That(system.Window!.Title, Is.EqualTo("The Wasteland Table"));
            Assert.That(system.Window.Resizable, Is.True);
            Assert.That(system.Window.Tree.Items.Count, Is.EqualTo(9));
            foreach (var id in component.Chapters.Keys)
            {
                system.Window.HandleClick(id);
                Assert.That(system.Window.LastEntry.Id, Is.EqualTo(id));
            }
            system.Window.Close();
        });
        await pair.Server.WaitPost(() => server.DeleteEntity(serverBook));
        await pair.CleanReturnAsync();
    }
}
