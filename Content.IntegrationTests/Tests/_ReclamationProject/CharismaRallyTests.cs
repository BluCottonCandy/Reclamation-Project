using System;
using Content.Shared._ReclamationProject.Charisma;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class CharismaRallyTests
{
    [Test]
    public async Task RallyBonusesApplyOnceAndEndWithoutChangingRecovery()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            try
            {
                var buff = em.EnsureComponent<CharismaRallyBuffComponent>(user);
                buff.ExpiresAt = timing.CurTime + TimeSpan.FromSeconds(20);
                var movement = new RefreshMovementSpeedModifiersEvent();
                em.EventBus.RaiseLocalEvent(user, movement);
                Assert.That(movement.WalkSpeedModifier, Is.EqualTo(1.2f).Within(0.001));
                Assert.That(movement.SprintSpeedModifier, Is.EqualTo(1.2f).Within(0.001));
                var stamina = new BeforeStaminaDamageEvent(20);
                em.EventBus.RaiseLocalEvent(user, ref stamina);
                Assert.That(stamina.Value, Is.EqualTo(15));
                stamina = new(-20);
                em.EventBus.RaiseLocalEvent(user, ref stamina);
                Assert.That(stamina.Value, Is.EqualTo(-20));
                var slowdown = new ModifySlowOnDamageSpeedEvent(0.5f);
                em.EventBus.RaiseLocalEvent(user, ref slowdown);
                Assert.That(slowdown.Speed, Is.EqualTo(1f));
                buff.ExpiresAt = timing.CurTime;
                stamina = new(20);
                em.EventBus.RaiseLocalEvent(user, ref stamina);
                Assert.That(stamina.Value, Is.EqualTo(20));
                slowdown = new(0.5f);
                em.EventBus.RaiseLocalEvent(user, ref slowdown);
                Assert.That(slowdown.Speed, Is.EqualTo(0.5f));
            }
            finally
            {
                em.DeleteEntity(user);
            }
        });
        await pair.CleanReturnAsync();
    }
}
