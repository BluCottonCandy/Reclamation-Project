using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._ReclamationProject;

public sealed class ArmorTowMovementTest : MovementTest
{
    protected override int Tiles => 20;

    [TestCase("ReclamationSupplyCrateRed", false)]
    [TestCase("ReclamationSupplyCrateRed", true)]
    [TestCase("MobHuman", false)]
    [TestCase("MobHuman", true)]
    public async Task HitchPreservesTravelSpeedAcrossTheFloor(string load, bool sprint)
    {
        EntityUid module = default;
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var suit = SEntMan.SpawnEntity("AtomCatsT45Blue", coords);
            module = AtomCatsGarageUtilityTest.Fit(SEntMan, coords, suit, "TowRig");
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(SPlayer, suit, "outerClothing", force: true));
        });
        await RunTicks(10);
        var sprintKey = Comp<InputMoverComponent>(Player).DefaultSprinting == sprint ? BoundKeyState.Down : BoundKeyState.Up;
        await SetKey(EngineKeyFunctions.Walk, sprintKey);
        await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
        await RunSeconds(1);
        float Position(EntityUid uid) => Transform.GetWorldPosition(uid).X;
        var start = Position(SPlayer);
        await RunSeconds(1);
        var freeTravel = Position(SPlayer) - start;
        Assert.That(Comp<InputMoverComponent>(Player).Sprinting, Is.EqualTo(sprint));
        Assert.That(freeTravel, Is.GreaterThan(0.3f));
        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            target = SEntMan.SpawnEntity(load, coords.Offset(new Vector2(-0.8f, 0f)));
            if (load == "MobHuman")
                SEntMan.System<StandingStateSystem>().Down(target);
            var hitch = new TargetGarageModEvent { Performer = SPlayer, Target = target };
            SEntMan.EventBus.RaiseLocalEvent(module, hitch);
            Assert.That(hitch.Handled);
        });
        await RunSeconds(1);
        start = Position(SPlayer);
        var targetStart = Position(target);
        await RunSeconds(1);
        var towTravel = Position(SPlayer) - start;
        var loadTravel = Position(target) - targetStart;
        await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);
        await SetKey(EngineKeyFunctions.Walk, BoundKeyState.Up);
        Assert.That(Comp<ArmorTowComponent>(Player).Target, Is.EqualTo(target));
        Assert.That(towTravel, Is.EqualTo(freeTravel).Within(0.02f), "distance travelled in one second");
        Assert.That(loadTravel, Is.EqualTo(towTravel).Within(0.1f), "the hitched load must follow");
    }
}
