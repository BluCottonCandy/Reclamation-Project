using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Construction.Components;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

public sealed class PaladinShieldCraftingTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [TestCase("ReclamationPaladinShield", 50, 2, 10, 0)]
    [TestCase("ReclamationPaladinGreatshield", 100, 5, 30, 2)]
    public async Task CraftPaladinShield(string recipe, int steel, int leather, int iron, int glass)
    {
        var coords = SEntMan.GetCoordinates(PlayerCoords);
        await Server.WaitPost(() => SEntMan.EnsureComponent<SpecialComponent>(SPlayer).BaseIntelligence = 5);
        await PlaceInHands(Steel, 24);
        for (var supplied = 25; supplied < steel; supplied += 25)
            await SpawnEntity((Steel, 25), coords);
        await SpawnEntity(("Leather", leather + 1), coords);
        await SpawnEntity(("N14Iron", iron), coords);
        if (glass > 0)
            await SpawnEntity((Glass, glass), coords);

        // Retry the same craft with the missing sheet and the original returned supplies.
        await CraftItem(recipe, shouldSucceed: false);
        await SpawnEntity((Steel, 1), coords);
        await CraftItem(recipe);
        var shield = await FindEntity(recipe);
        var nearby = await DoEntityLookup(LookupFlags.Uncontained | LookupFlags.Contained);
        await Server.WaitAssertion(() =>
        {
            var construction = SEntMan.GetComponent<ConstructionComponent>(shield);
            Assert.That(construction.Graph.Id, Is.EqualTo(recipe));
            Assert.That(construction.Node, Is.EqualTo("shield"));
            var stacks = nearby.Where(uid => SEntMan.HasComponent<StackComponent>(uid))
                .Select(uid => SEntMan.GetComponent<StackComponent>(uid)).ToArray();
            Assert.That(stacks, Has.Length.EqualTo(1));
            Assert.That(stacks[0].StackTypeId.Id, Is.EqualTo("Leather"));
            Assert.That(stacks[0].Count, Is.EqualTo(1));
        });
    }
}
