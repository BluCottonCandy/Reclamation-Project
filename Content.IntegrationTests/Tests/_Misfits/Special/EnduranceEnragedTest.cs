using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Content.Shared.Movement.Systems;
using Content.Shared._ReclamationProject.Endurance;
using Content.Server._ReclamationProject.Endurance;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Chemistry;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Misfits.Special;

[TestFixture]
public sealed class EnduranceEnragedTest
{
    [Test]
    public async Task RageDefersDeathRetainsDamageAndBlocksTreatment()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<SpecialComponent>(mob);
            var special = entities.System<SharedSpecialSystem>();
            var states = entities.System<MobStateSystem>();
            var damage = entities.System<DamageableSystem>();
            special.TrySetBase(mob, SpecialStat.Endurance, 9);
            states.ChangeMobState(mob, MobState.Critical);
            Assert.That(states.IsCritical(mob), Is.True);
            Assert.That(entities.HasComponent<EnduranceEnragedComponent>(mob), Is.False);
            states.ChangeMobState(mob, MobState.Alive);
            special.TrySetBase(mob, SpecialStat.Endurance, 10);
            damage.TryChangeDamage(mob, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 1000 } },
                ignoreResistances: true, doPartDamage: false);
            var rage = entities.GetComponent<EnduranceEnragedComponent>(mob);
            Assert.That(states.IsAlive(mob), Is.True);
            Assert.That(rage.Active, Is.True);
            Assert.That(entities.GetComponent<Content.Shared.Mobs.Components.MobThresholdsComponent>(mob).CurrentThresholdState, Is.EqualTo(MobState.Alive));
            Assert.That(rage.ReadyAt - rage.EndsAt, Is.EqualTo(TimeSpan.FromSeconds(290)));
            Assert.That(rage.EndsAt - pair.Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime,
                Is.EqualTo(TimeSpan.FromSeconds(10)));
            var before = entities.GetComponent<DamageableComponent>(mob).TotalDamage;
            damage.TryChangeDamage(mob, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 8 } },
                ignoreResistances: true, doPartDamage: false);
            Assert.That((float) (entities.GetComponent<DamageableComponent>(mob).TotalDamage - before), Is.EqualTo(10f));
            damage.TryChangeDamage(mob, new DamageSpecifier { DamageDict = new() { ["Blunt"] = -10 } },
                ignoreResistances: true, doPartDamage: false);
            Assert.That((float) (entities.GetComponent<DamageableComponent>(mob).TotalDamage - before), Is.EqualTo(10f));
            var speed = new RefreshMovementSpeedModifiersEvent();
            speed.ModifySpeed(0.5f, 0.5f);
            entities.EventBus.RaiseLocalEvent(mob, speed);
            Assert.That(speed.WalkSpeedModifier, Is.GreaterThanOrEqualTo(1f));
            Assert.That(speed.SprintSpeedModifier, Is.GreaterThanOrEqualTo(1f));
            var treatment = new MedicalTreatmentAttemptEvent();
            entities.EventBus.RaiseLocalEvent(mob, ref treatment);
            Assert.That(treatment.Cancelled, Is.True);
            rage.EndsAt = TimeSpan.Zero;
            entities.System<EnduranceEnragedSystem>().Update(0);
            Assert.That(states.IsDead(mob), Is.True);
            Assert.That(rage.Active, Is.False);
            treatment = new MedicalTreatmentAttemptEvent();
            entities.EventBus.RaiseLocalEvent(mob, ref treatment);
            Assert.That(treatment.Cancelled, Is.False);
            states.ChangeMobState(mob, MobState.Alive);
            states.ChangeMobState(mob, MobState.Dead);
            Assert.That(states.IsDead(mob), Is.True, "Cooldown prevents another rage.");
            entities.DeleteEntity(mob);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StaleMeleeCoordinatesAreRejected()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var attacker = entities.SpawnEntity("MobHuman", map.GridCoords);
            var target = entities.SpawnEntity("MobHuman", map.GridCoords);
            var interaction = entities.System<SharedInteractionSystem>();
            Assert.That(interaction.InRangeUnobstructed(attacker, target, EntityCoordinates.Invalid, Angle.Zero), Is.False);
            var coordinates = entities.GetComponent<TransformComponent>(target).Coordinates;
            entities.DeleteEntity(target);
            Assert.That(interaction.InRangeUnobstructed(attacker, target, coordinates, Angle.Zero), Is.False);
            entities.DeleteEntity(attacker);
        });
        await pair.CleanReturnAsync();
    }
}
