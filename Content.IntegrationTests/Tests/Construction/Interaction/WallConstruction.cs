using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Misfits.Special.Components;

namespace Content.IntegrationTests.Tests.Construction.Interaction;

public sealed class WallConstruction : InteractionTest
{
    public const string Girder = "Girder";
    public const string WallSolid = "WallSolid";
    public const string Wall = "Wall";

    [SetUp]
    public async Task SetUpConstructionStats()
    {
        // Reclamation requires SPECIAL intelligence to construct and deconstruct structures.
        await Server.WaitPost(() => SEntMan.EnsureComponent<SpecialComponent>(SPlayer));
        await RunTicks(5);
    }

    [Test]
    public async Task ConstructWall()
    {
        await StartConstruction(Wall);
        await InteractUsing(Steel, 2);
        Assert.That(Hands.ActiveHandEntity, Is.Null);
        ClientAssertPrototype(Girder, Target);
        await InteractUsing(Steel, 2);
        Assert.That(Hands.ActiveHandEntity, Is.Null);
        AssertPrototype(WallSolid);
    }

    [Test]
    public async Task DeconstructWall()
    {
        await StartDeconstruction(WallSolid);
        await InteractUsing(Weld);
        AssertPrototype(Girder);
        await Interact(Wrench, Screw);
        AssertDeleted();
        await AssertEntityLookup((Steel, 4));
    }
}
