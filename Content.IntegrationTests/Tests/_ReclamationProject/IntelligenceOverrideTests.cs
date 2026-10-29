using System;
using System.Numerics;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._ReclamationProject.Intelligence;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class IntelligenceOverrideTests
{
    [Test]
    public async Task OverrideBypassesDoorPowerWithoutChangingOrdinaryEmags()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var door = em.SpawnEntity("Airlock", map.GridCoords.Offset(new Vector2(1, 0)));
            var wood = em.SpawnEntity("N14DoorWoodSecure", map.GridCoords.Offset(new Vector2(0, 1)));
            em.EnsureComponent<SpecialComponent>(user);
            try
            {
                em.System<SharedSpecialSystem>().TrySetBase(user, SpecialStat.Intelligence, 10);
                var state = em.GetComponent<IntelligenceOverrideComponent>(user);
                var action = new IntelligenceOverrideActionEvent { Performer = user, Target = wood };
                em.EventBus.RaiseLocalEvent(user, action);
                Assert.That(action.Handled, Is.False);
                Assert.That(state.NextUseAt, Is.EqualTo(TimeSpan.Zero));
                Assert.That(em.System<EmagSystem>().DoEmagEffect(user, door), Is.False);
                action = new() { Performer = user, Target = door };
                em.EventBus.RaiseLocalEvent(user, action);
                Assert.That(action.Handled, Is.True);
                Assert.That(em.HasComponent<EmaggedComponent>(door), Is.True);
                Assert.That(state.NextUseAt - timing.CurTime, Is.EqualTo(TimeSpan.FromMinutes(1)));
            }
            finally
            {
                em.DeleteEntity(user);
                em.DeleteEntity(door);
                em.DeleteEntity(wood);
            }
        });
        await pair.CleanReturnAsync();
    }
}
