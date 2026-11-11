using System.Linq;
using Content.Server._ReclamationProject.Lockpick;
using Content.Shared._ReclamationProject.Lockpick;
using Content.Shared.Doors.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - lockpicking: wooden doors are 3 plain pins, metal doors have a spool and a serrated pin,
/// the server only sets the binding pin inside its band, oversets jam, spools need a false set then light tension,
/// snapping uses up a pick, and a picked door opens. Airlocks can't be picked.
/// </summary>
[TestFixture]
public sealed class LockpickTest
{
    [Test]
    public async Task PickingDoors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var sys = entities.System<LockpickSystem>();

        EntityUid user = default, pick = default, wood = default, metal = default;

        void Release(int pin, float lift, LockpickTension tension = LockpickTension.Right)
        {
            entities.EventBus.RaiseLocalEvent(pick, new LockpickReleaseMessage { Pin = pin, Lift = lift, Tension = tension, Actor = user });
        }

        await server.WaitAssertion(() =>
        {
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            pick = entities.SpawnEntity("ReclamationLockpickSet", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, pick));
            wood = entities.SpawnEntity("N14DoorWoodSecureLockedMayor", map.GridCoords);
            metal = entities.SpawnEntity("N14DoorMetalSecureLockedIron", map.GridCoords);

            Assert.That(sys.IsPickable(wood), "a locked wooden secure door should be pickable");
            Assert.That(sys.IsPickable(metal), "a locked metal secure door should be pickable");
            var airlock = entities.SpawnEntity("Airlock", map.GridCoords);
            Assert.That(sys.IsPickable(airlock), Is.False, "powered airlocks are for the emag");

            sys.Start((pick, entities.GetComponent<LockpickComponent>(pick)), wood, user);
            Assert.That(sys.TryGetLock(pick, out var pins, out _, out _));
            Assert.That(pins, Has.Count.EqualTo(3));
            Assert.That(pins.All(p => p.Type == LockPinType.Plain), "wooden locks only have plain pins");
        });

        // Wrong pin, too low, overset, reset.
        int first = 0, second = 0;
        await server.WaitAssertion(() =>
        {
            sys.TryGetLock(pick, out _, out var order, out _);
            first = order[0];
            second = order[1];
            Release(second, 999);
            sys.TryGetLock(pick, out var pins, out _, out _);
            Assert.That(pins[second].Set || pins[second].Jammed, Is.False, "a non-binding pin reacted");
        });
        await pair.RunTicksSync(15);
        await server.WaitAssertion(() =>
        {
            Release(first, 0);
            sys.TryGetLock(pick, out var pins, out _, out _);
            Assert.That(pins[first].Set, Is.False, "an unlifted pin set");
        });
        await pair.RunTicksSync(15);
        await server.WaitAssertion(() =>
        {
            Release(first, 999);
            sys.TryGetLock(pick, out var pins, out _, out _);
            Assert.That(pins[first].Jammed, "pushing far past the line should jam");
            entities.EventBus.RaiseLocalEvent(pick, new LockpickResetMessage { Actor = user });
            Assert.That(pins[first].Jammed, Is.False, "release tension didn't clear the jam");
        });

        // Set every pin in order: the door opens.
        for (var step = 0; step < 3; step++)
        {
            await pair.RunTicksSync(15);
            await server.WaitAssertion(() =>
            {
                sys.TryGetLock(pick, out var pins, out var order, out var next);
                var pin = order[next];
                Release(pin, LockpickGeometry.SetLift(pins[pin].Length));
            });
        }
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(sys.TryGetLock(pick, out _, out _, out _), Is.False, "the session should end on unlock");
            Assert.That(entities.GetComponent<DoorComponent>(wood).State, Is.Not.EqualTo(DoorState.Closed), "the picked door didn't open");

            sys.Start((pick, entities.GetComponent<LockpickComponent>(pick)), metal, user);
            sys.TryGetLock(pick, out var pins, out _, out _);
            Assert.That(pins, Has.Count.EqualTo(5));
            Assert.That(pins.Count(p => p.Type == LockPinType.Spool), Is.EqualTo(1));
            Assert.That(pins.Count(p => p.Type == LockPinType.Serrated), Is.EqualTo(1));
        });

        // Walk the metal lock: the spool needs a false set, then light tension.
        var sawSpool = false;
        var triedRight = false;
        for (var step = 0; step < 12; step++)
        {
            var finished = false;
            await pair.RunTicksSync(15);
            await server.WaitAssertion(() =>
            {
                if (!sys.TryGetLock(pick, out var pins, out var order, out var next))
                {
                    finished = true;
                    return;
                }

                var pin = order[next];
                var state = pins[pin];
                var lift = LockpickGeometry.SetLift(state.Length);
                if (state.Type == LockPinType.Spool && !state.FalseSet)
                {
                    Release(pin, lift);
                    Assert.That(state.FalseSet && !state.Set, "the first spool set should be a false set");
                    sawSpool = true;
                }
                else if (state.Type == LockPinType.Spool && !triedRight)
                {
                    Release(pin, lift, LockpickTension.Right);
                    Assert.That(state.Set, Is.False, "a spool set without easing tension");
                    triedRight = true;
                }
                else if (state.Type == LockPinType.Spool)
                {
                    Release(pin, lift, LockpickTension.Light);
                    Assert.That(state.Set, "a spool didn't set on light tension after a false set");
                }
                else
                {
                    Release(pin, lift);
                }
            });
            if (finished)
                break;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(sawSpool, "never met the spool pin");
            Assert.That(entities.GetComponent<DoorComponent>(metal).State, Is.Not.EqualTo(DoorState.Closed), "the metal door didn't open");

            // Snapping a pick uses one up.
            var wood2 = entities.SpawnEntity("N14DoorWoodSecureLockedMayor", map.GridCoords);
            sys.Start((pick, entities.GetComponent<LockpickComponent>(pick)), wood2, user);
            entities.EventBus.RaiseLocalEvent(pick, new LockpickSnapMessage { Actor = user });
            Assert.That(entities.GetComponent<LockpickComponent>(pick).Picks, Is.EqualTo(4));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InstituteEmagOpensUnpoweredBunkerDoors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        EntityUid bunker = default;
        await server.WaitAssertion(() =>
        {
            var emagSys = entities.System<EmagSystem>();
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var emag = entities.SpawnEntity("InstituteEmag", map.GridCoords);

            // The test map has no power at all, like most wasteland bunkers.
            bunker = entities.SpawnEntity("N14DoorBunker", map.GridCoords);
            Assert.That(emagSys.TryUseEmag(emag, user, bunker), "the Institute emag didn't work on an unpowered bunker door");

            var wood = entities.SpawnEntity("N14DoorWoodSecureLockedMayor", map.GridCoords);
            Assert.That(emagSys.TryUseEmag(emag, user, wood), Is.False, "the emag shouldn't work on doors without power systems");
        });
        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<DoorComponent>(bunker).State, Is.AnyOf(DoorState.Open, DoorState.Opening),
                "the emagged bunker door didn't open"));

        await pair.CleanReturnAsync();
    }
}
