using System;
using System.Numerics;
using Content.Server._ReclamationProject.Strength;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._ReclamationProject.Strength;
using Content.Shared.Damage;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class StrengthJuggernautTests
{
    [Test]
    public async Task JuggernautRequiresTenStrengthAndExpiresIntoFourSecondStun()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var target = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1, 0)));
            var weapon = em.SpawnEntity("N14CombatKnife", map.GridCoords);
            em.EnsureComponent<SpecialComponent>(user);
            try
            {
                var stats = em.System<SharedSpecialSystem>();
                stats.TrySetBase(user, SpecialStat.Strength, 9);
                var state = em.EnsureComponent<StrengthJuggernautComponent>(user);
                Assert.That(state.Action, Is.Null);
                var action = new StrengthJuggernautActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref action);
                Assert.That(action.Handled, Is.False);
                stats.TrySetBase(user, SpecialStat.Strength, 10);
                Assert.That(state.Action, Is.Not.Null);
                action = new() { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref action);
                Assert.That(action.Handled, Is.True);
                Assert.That(state.EndsAt - timing.CurTime, Is.EqualTo(TimeSpan.FromSeconds(15)));
                Assert.That(state.NextUseAt - timing.CurTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                var damage = new DamageSpecifier();
                damage.DamageDict.Add("Blunt", 20);
                damage.DamageDict.Add("Structural", 40);
                var hit = new MeleeHitEvent(new() { target }, user, weapon, damage, null);
                em.EventBus.RaiseLocalEvent(weapon, hit);
                var result = DamageSpecifier.ApplyModifierSets(damage + hit.BonusDamage, hit.ModifiersList);
                Assert.That(result.DamageDict["Blunt"].Float(), Is.EqualTo(25));
                Assert.That(result.DamageDict["Structural"].Float(), Is.EqualTo(50), "Structural damage must not receive the bonus twice.");
                Assert.That(em.HasComponent<KnockedDownComponent>(target), Is.True);
                Assert.That(em.GetComponent<PhysicsComponent>(target).LinearVelocity.X, Is.GreaterThan(0));
                var cooldown = state.NextUseAt;
                stats.TrySetBase(user, SpecialStat.Strength, 9);
                Assert.That(state.Action, Is.Null);
                stats.TrySetBase(user, SpecialStat.Strength, 10);
                Assert.That(state.NextUseAt, Is.EqualTo(cooldown));
                state.EndsAt = timing.CurTime - TimeSpan.FromSeconds(1);
                em.System<StrengthJuggernautSystem>().Update(0);
                Assert.That(em.System<SharedStrengthJuggernautSystem>().IsActive(user), Is.False);
                Assert.That(em.HasComponent<StunnedComponent>(user), Is.True);
                Assert.That(em.System<StatusEffectsSystem>().TryGetTime(user, "Stun", out var stun), Is.True);
                Assert.That((stun!.Value.Item2 - timing.CurTime).TotalSeconds, Is.EqualTo(4).Within(0.01));
                hit = new(new(), user, weapon, damage, null);
                em.EventBus.RaiseLocalEvent(weapon, hit);
                result = DamageSpecifier.ApplyModifierSets(damage, hit.ModifiersList);
                Assert.That(result.DamageDict["Blunt"].Float(), Is.EqualTo(20));
                action = new() { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref action);
                Assert.That(action.Handled, Is.False);
                Assert.That(state.NextUseAt, Is.EqualTo(cooldown));
            }
            finally
            {
                em.DeleteEntity(user);
                em.DeleteEntity(target);
                em.DeleteEntity(weapon);
            }
        });
        await pair.CleanReturnAsync();
    }
}
