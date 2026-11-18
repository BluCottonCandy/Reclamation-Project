using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class GhoulrillaRecoveryTest
{
    [TestCase("PackGhoulrilla")]
    [TestCase("PackGhoulrillaKing")]
    [TestCase("PackHound")]
    public async Task RecoversFromCriticalInjuriesWithoutTreatment(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var ticks = server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid pet = default;
        await server.WaitAssertion(() =>
        {
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            entities.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));
            pet = entities.SpawnEntity(prototype, map.GridCoords);
            var threshold = entities.System<MobThresholdSystem>().GetThresholdForState(pet, MobState.Critical);
            entities.System<DamageableSystem>().TryChangeDamage(pet,
                new DamageSpecifier { DamageDict = { ["Blunt"] = threshold + 9 } },
                ignoreResistances: true, doPartDamage: false);
            Assert.That(entities.System<MobStateSystem>().IsCritical(pet), Is.True);
            Assert.That(entities.GetComponent<StandingStateComponent>(pet).CurrentState, Is.EqualTo(StandingState.Lying));
        });

        // Stay down through multiple breathing cycles so spontaneous suffocation would be observable.
        await server.WaitRunTicks(ticks * 12);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<MobStateSystem>().IsCritical(pet), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(pet).Damage.DamageDict["Asphyxiation"], Is.EqualTo(FixedPoint2.Zero));
        });

        await server.WaitRunTicks(ticks * 65);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<MobStateSystem>().IsAlive(pet), Is.True);
            Assert.That(entities.GetComponent<StandingStateComponent>(pet).CurrentState, Is.EqualTo(StandingState.Standing));

            var lethal = entities.System<MobThresholdSystem>().GetThresholdForState(pet, MobState.Dead);
            entities.System<DamageableSystem>().TryChangeDamage(pet,
                new DamageSpecifier { DamageDict = { ["Heat"] = lethal + 15 } },
                ignoreResistances: true, doPartDamage: false);
            Assert.That(entities.System<MobStateSystem>().IsDead(pet), Is.True);
        });
        await server.WaitRunTicks(ticks * 5);
        await server.WaitAssertion(() => Assert.That(entities.System<MobStateSystem>().IsDead(pet), Is.True));
        await pair.CleanReturnAsync();
    }
}
