using System;
using System.Linq;
using System.Numerics;
using Content.Server._ReclamationProject.Triggermen;
using Content.Shared._ReclamationProject.Triggermen;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class PoolTableTests
{
    private static void Move(IEntityManager entities, EntityUid table, EntityUid actor,
        PoolAction action, Vector2 point = default, int power = 0)
        => entities.EventBus.RaiseLocalEvent(table, new PoolMove(point, power, action)
            { Actor = actor, UiKey = PoolKey.Key });

    [Test]
    public async Task TurnControlsRackingAndDepartureAreConsistent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var first = entities.SpawnEntity("MobHuman", map.GridCoords);
            var second = entities.SpawnEntity("MobHuman", map.GridCoords);
            var uid = entities.SpawnEntity("TriggermenPoolTable", map.GridCoords);
            var table = entities.GetComponent<PoolTableComponent>(uid);
            Move(entities, uid, first, PoolAction.Join);
            Assert.That(table.Players, Is.EqualTo(new[] { first }));
            var initial = (Vector2[]) table.Balls.Clone();
            Assert.That(initial.Distinct().Count(), Is.EqualTo(16));
            table.Balls[0] = new Vector2(70, 70);
            Move(entities, uid, first, PoolAction.Rack);
            Assert.That(table.Balls, Is.EqualTo(initial), "Solo practice can be reracked before the eight is sunk.");
            Move(entities, uid, first, PoolAction.Shoot, new Vector2(float.NaN, 120), 60);
            Move(entities, uid, first, PoolAction.Shoot, new Vector2(335, 120), 101);
            Move(entities, uid, second, PoolAction.Shoot, new Vector2(335, 120), 60);
            Assert.That(table.Moving, Is.False);
            Move(entities, uid, second, PoolAction.Join);
            Assert.That(table.Players.Count, Is.EqualTo(2));
            table.Balls[0] = new Vector2(100, 110);
            Move(entities, uid, first, PoolAction.Rack);
            Assert.That(table.Balls[0], Is.EqualTo(new Vector2(100, 110)), "A player cannot erase an unfinished two-player rack.");
            Move(entities, uid, second, PoolAction.Shoot, new Vector2(335, 120), 60);
            Assert.That(table.Moving, Is.False, "Only the current player can shoot.");
            Move(entities, uid, first, PoolAction.Shoot, new Vector2(335, 120), 60);
            Assert.That(table.Moving, Is.True);
            entities.EventBus.RaiseLocalEvent(uid, new BoundUIClosedEvent(PoolKey.Key, uid, first));
            Assert.Multiple(() =>
            {
                Assert.That(table.Players, Is.EqualTo(new[] { second }));
                Assert.That(table.Moving, Is.False);
                Assert.That(table.Velocity.All(v => v == Vector2.Zero), Is.True);
                Assert.That(table.Turn, Is.Zero);
            });
            entities.DeleteEntity(uid);
            entities.DeleteEntity(first);
            entities.DeleteEntity(second);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PhysicsRetainsElapsedTimeAcrossDifferentFrameSizes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var uid = entities.SpawnEntity("TriggermenPoolTable", map.GridCoords);
            var table = entities.GetComponent<PoolTableComponent>(uid);
            var system = entities.System<PoolTableSystem>();
            Move(entities, uid, user, PoolAction.Join);
            Move(entities, uid, user, PoolAction.Shoot, new Vector2(335, 120), 60);
            for (var i = 0; i < 100; i++)
                system.Update(0.01f);
            var positions = (Vector2[]) table.Balls.Clone();
            var velocity = (Vector2[]) table.Velocity.Clone();
            table.Moving = false;
            Move(entities, uid, user, PoolAction.Rack);
            Move(entities, uid, user, PoolAction.Shoot, new Vector2(335, 120), 60);
            for (var i = 0; i < 10; i++)
                system.Update(0.1f);
            for (var i = 0; i < 16; i++)
            {
                Assert.That(Vector2.Distance(positions[i], table.Balls[i]), Is.LessThan(0.01f));
                Assert.That(Vector2.Distance(velocity[i], table.Velocity[i]), Is.LessThan(0.01f));
            }
            Assert.That(table.FirstHit, Is.GreaterThan(0), "A normal-power break must reach the rack.");
            entities.DeleteEntity(uid);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScratchesRequireSafeCuePlacementAndEightBallRespectsTheGroup()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var first = entities.SpawnEntity("MobHuman", map.GridCoords);
            var second = entities.SpawnEntity("MobHuman", map.GridCoords);
            var metadata = entities.System<MetaDataSystem>();
            metadata.SetEntityName(first, "Pool First");
            metadata.SetEntityName(second, "Pool Second");
            var uid = entities.SpawnEntity("TriggermenPoolTable", map.GridCoords);
            var table = entities.GetComponent<PoolTableComponent>(uid);
            var system = entities.System<PoolTableSystem>();
            Move(entities, uid, first, PoolAction.Join);
            Move(entities, uid, second, PoolAction.Join);
            table.Balls[0] = new Vector2(20, 20);
            Move(entities, uid, first, PoolAction.Shoot, Vector2.Zero, 30);
            Settle();
            Assert.That(table.Scratch, Is.True);
            Assert.That(table.Turn, Is.EqualTo(1));
            Assert.That(table.BallInHand, Is.True);
            Move(entities, uid, second, PoolAction.Shoot, new Vector2(335, 120), 50);
            Assert.That(table.Moving, Is.False);
            Move(entities, uid, second, PoolAction.PlaceCue, table.Balls[1]);
            Assert.That(table.BallInHand, Is.True, "Cannot place the cue on another ball.");
            Move(entities, uid, second, PoolAction.PlaceCue, Vector2.Zero);
            Assert.That(table.BallInHand, Is.True, "Cannot place the cue in a pocket.");
            Move(entities, uid, second, PoolAction.PlaceCue, new Vector2(100, 100));
            Assert.That(table.BallInHand, Is.False);
            Assert.That(table.Balls[0], Is.EqualTo(new Vector2(100, 100)));

            foreach (var cleared in new[] { false, true })
            {
                Array.Fill(table.Balls, new Vector2(-1, -1));
                Array.Clear(table.Velocity);
                table.Balls[0] = new Vector2(240, 35);
                table.Balls[8] = new Vector2(240, 14);
                if (!cleared)
                    table.Balls[1] = new Vector2(335, 120);
                table.FirstPlayerSolids = true;
                table.Turn = 0;
                table.Finished = false;
                Move(entities, uid, first, PoolAction.Shoot, new Vector2(240, 0), 30);
                Settle();
                Assert.That(table.Balls[8].X, Is.LessThan(0));
                Assert.That(table.Finished, Is.True);
                Assert.That(table.Result, Does.StartWith(cleared ? "Pool First wins!" : "Pool Second wins!"));
            }
            entities.DeleteEntity(uid);
            entities.DeleteEntity(first);
            entities.DeleteEntity(second);

            void Settle()
            {
                for (var i = 0; i < 2000 && table.Moving; i++)
                    system.Update(0.01f);
                Assert.That(table.Moving, Is.False, "The shot must eventually settle.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
