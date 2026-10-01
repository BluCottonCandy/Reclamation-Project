// #Misfits Add - Integration tests: verify N14 custom chems apply their expected status effects.
#nullable enable

using Content.Server.Body.Systems;
using Content.Shared._Misfits.DrugEffects;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Misfits;

[TestFixture]
public sealed class DamageResistMixtureTests
{
    [Test]
    public async Task DamageResistMixture_ReducesIncomingDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var bloodstreamSystem = entityManager.System<BloodstreamSystem>();
        var damageableSystem = entityManager.System<DamageableSystem>();

        EntityUid control = default;
        EntityUid treated = default;

        await server.WaitAssertion(() =>
        {
            control = entityManager.SpawnEntity("MobHuman", map.GridCoords);
            treated = entityManager.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(
                bloodstreamSystem.TryAddToChemicals(treated, new Solution("DamageResistMixture", FixedPoint2.New(5))),
                Is.True,
                "DamageResistMixture should enter the treated mob's bloodstream.");
        });

        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(
                entityManager.HasComponent<MedXProtectionComponent>(treated),
                Is.True,
                "DamageResistMixture should apply MedXProtection after metabolism ticks.");

            var blunt = prototypeManager.Index<DamageTypePrototype>("Blunt");
            var incomingDamage = new DamageSpecifier(blunt, FixedPoint2.New(40));
            var controlBefore = entityManager.GetComponent<DamageableComponent>(control).TotalDamage;
            var treatedBefore = entityManager.GetComponent<DamageableComponent>(treated).TotalDamage;

            var controlDelta = damageableSystem.TryChangeDamage(control, incomingDamage);
            var treatedDelta = damageableSystem.TryChangeDamage(treated, incomingDamage);

            var controlAfter = entityManager.GetComponent<DamageableComponent>(control).TotalDamage;
            var treatedAfter = entityManager.GetComponent<DamageableComponent>(treated).TotalDamage;

            Assert.Multiple(() =>
            {
                Assert.That(controlDelta, Is.Not.Null);
                Assert.That(treatedDelta, Is.Not.Null);

                Assert.That(controlAfter - controlBefore, Is.EqualTo(FixedPoint2.New(40)));
                Assert.That(treatedAfter - treatedBefore, Is.EqualTo(FixedPoint2.New(30)));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HealingPowder_AppliesHazeStatus()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var bloodstreamSystem = entityManager.System<BloodstreamSystem>();

        EntityUid treated = default;

        await server.WaitAssertion(() =>
        {
            treated = entityManager.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(
                bloodstreamSystem.TryAddToChemicals(treated, new Solution("HealingPowder", FixedPoint2.New(5))),
                Is.True,
                "HealingPowder should enter the treated mob's bloodstream.");
        });

        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(
                entityManager.HasComponent<HealingPowderHazeComponent>(treated),
                Is.True,
                "HealingPowder should apply HealingPowderHaze after metabolism ticks.");
        });

        await pair.CleanReturnAsync();
    }
}