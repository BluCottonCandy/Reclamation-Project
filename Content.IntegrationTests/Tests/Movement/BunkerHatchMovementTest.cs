using Content.Shared._Reclamation.Bunkers;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.Movement;

public sealed class BunkerHatchMovementTest : MovementTest
{
    [TestCase("N14CryoBunkerLadder")]
    [TestCase("N14N14CryoBunkerLadderE")]
    [TestCase("ReclamationPersonalBunkerExit")]
    public async Task WalkAcrossBunkerAccess(string prototype)
    {
        var hatch = await SpawnTarget(prototype);
        Assert.That(Delta(), Is.Positive);
        await Move(DirectionFlag.East, 1.5f);
        Assert.That(Delta(), Is.LessThan(-0.5f));

        if (SEntMan.HasComponent<PersonalBunkerEntranceComponent>(ToServer(hatch)))
        {
            await Interact();
            Assert.That(IsUiOpen(PersonalBunkerUiKey.Key), Is.True);
        }

        // A re-enabled physics body must still have a walkable fixture.
        await Server.WaitPost(() => SEntMan.System<SharedPhysicsSystem>().SetCanCollide(ToServer(hatch), true));
        await Move(DirectionFlag.West, 2f);
        Assert.That(Delta(), Is.GreaterThan(0.5f));
    }
}
