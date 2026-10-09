using System.Collections.Generic;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Richtext;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class WastelandMedicineGuideTest
{
    [Test]
    public async Task MedicineChaptersLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var prototypes = client.ResolveDependency<IPrototypeManager>();
        var resources = client.ResolveDependency<IResourceManager>();
        var parser = client.ResolveDependency<DocumentParsingManager>();
        var root = prototypes.Index<GuideEntryPrototype>("N14Medicine");

        Assert.That(root.Children, Has.Count.EqualTo(6));
        var entries = new List<string> { root.ID };
        entries.AddRange(root.Children);
        foreach (var id in entries)
        {
            await client.WaitAssertion(() =>
            {
                var entry = prototypes.Index<GuideEntryPrototype>(id);
                using var reader = resources.ContentFileReadText(entry.Text);
                Assert.That(parser.TryAddMarkup(new Document(), reader.ReadToEnd()), Is.True,
                    $"Failed to parse medicine chapter: {id}");
            });
            await client.WaitRunTicks(1);
        }

        await pair.CleanReturnAsync();
    }
}
