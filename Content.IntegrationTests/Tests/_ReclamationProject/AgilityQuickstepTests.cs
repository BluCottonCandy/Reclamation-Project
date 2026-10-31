using System;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._ReclamationProject.Agility;
using Content.Shared.Damage.Components;
using Content.Shared._Misfits.MeleeCharge;
using Content.Shared.Throwing;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Content.Shared.DoAfter;
using Content.Shared.Medical;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AgilityQuickstepTests
{
    [Test]
    public async Task QuickstepAlsoRunsOnTheClient()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Client.ResolveDependency<IEntityManager>();
        await pair.Client.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            try
            {
                var user = em.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
                em.EnsureComponent<SpecialComponent>(user);
                em.EnsureComponent<AgilityQuickstepComponent>(user);
                em.System<SharedSpecialSystem>().TrySetBase(user, SpecialStat.Agility, 10);
                var action = new AgilityQuickstepActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, action);
                Assert.That(action.Handled, Is.True, "Quickstep must not wait for a server-only handler.");
                Assert.That(em.HasComponent<ThrownItemComponent>(user), Is.True);
                var velocity = em.GetComponent<PhysicsComponent>(user).LinearVelocity;
                em.System<MeleeChargeSystem>().PerformDash(user, new EntityCoordinates(map, new Vector2(5, 0)));
                Assert.That(em.GetComponent<PhysicsComponent>(user).LinearVelocity, Is.EqualTo(velocity),
                    "An overlapping charge must not apply its impulse twice.");
            }
            finally
            {
                em.DeleteEntity(map);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AgilityTransfersItsSpeedCurveToTimedActionsAndQuickstepCostsNoStamina()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<SpecialComponent>(user);
            try
            {
                var special = em.System<SharedSpecialSystem>();
                var speeds = em.System<SpecialDoAfterSystem>();
                var delta = new[] { -5f, -3.5f, -2.25f, -1f, 0f, 1f, 2.25f, 3.75f, 5.5f, 7.5f };
                for (var level = 1; level <= 10; level++)
                {
                    special.TrySetBase(user, SpecialStat.Agility, level);
                    Assert.That(speeds.GetSpeed(user), Is.EqualTo(1 + delta[level - 1] * 0.01f).Within(0.0001));
                    var movement = new RefreshMovementSpeedModifiersEvent();
                    em.EventBus.RaiseLocalEvent(user, movement);
                    Assert.That(movement.WalkSpeedModifier, Is.EqualTo(1f).Within(0.0001));
                    Assert.That(movement.SprintSpeedModifier, Is.EqualTo(1f).Within(0.0001));
                }
                var input = new DoAfterArgs(em, user, 10f, new HealingDoAfterEvent(), user) { NeedHand = false };
                Assert.That(em.System<SharedDoAfterSystem>().TryStartDoAfter(input, out var id), Is.True);
                var actual = em.GetComponent<DoAfterComponent>(user).DoAfters[id!.Value.Index];
                Assert.That(actual.Args.Delay.TotalSeconds, Is.EqualTo(10 / 1.075).Within(0.0001));
                Assert.That(input.Delay, Is.EqualTo(TimeSpan.FromSeconds(10)));
                var stamina = em.GetComponent<StaminaComponent>(user).StaminaDamage;
                var action = new AgilityQuickstepActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, action);
                Assert.That(action.Handled, Is.True);
                Assert.That(em.HasComponent<ThrownItemComponent>(user), Is.True);
                Assert.That(em.GetComponent<StaminaComponent>(user).StaminaDamage, Is.EqualTo(stamina));
                Assert.That(em.GetComponent<AgilityQuickstepComponent>(user).NextUseAt - timing.CurTime, Is.EqualTo(TimeSpan.FromSeconds(30)));
            }
            finally
            {
                em.DeleteEntity(user);
            }
        });
        await pair.CleanReturnAsync();
    }
}
