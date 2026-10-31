using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Misfits.Special;

[TestFixture]
public sealed class EnduranceHealthBonusTest
{
    [Test]
    public async Task OriginalHealthCurveRecalculatesWithoutStacking()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            var special = entities.EnsureComponent<SpecialComponent>(mob);
            var system = entities.System<SharedSpecialSystem>();
            var thresholds = entities.System<MobThresholdSystem>();
            Assert.That(system.TrySetBase(mob, SpecialStat.Endurance, 5), Is.True);
            var states = new[] { MobState.SoftCritical, MobState.Critical, MobState.Dead };
            var baseline = states.ToDictionary(state => state,
                state => (float) thresholds.GetThresholdForState(mob, state));

            void Check(int endurance, float expected)
            {
                Assert.That(system.TrySetBase(mob, SpecialStat.Endurance, endurance), Is.True);
                Assert.That(special.AppliedHealthThresholdModifier, Is.EqualTo(expected).Within(0.002f));
                foreach (var state in states)
                {
                    if (baseline[state] == 0f)
                        continue;
                    Assert.That((float) thresholds.GetThresholdForState(mob, state),
                        Is.EqualTo(baseline[state] + expected).Within(0.02f), state.ToString());
                }
            }

            Check(1, -13.3333335f);
            Check(2, -9.333333f);
            Check(3, -6f);
            Check(4, -2.6666667f);
            Check(5, 0f);
            Check(6, 2.6666667f);
            Check(7, 6f);
            Check(8, 10f);
            Check(9, 14.666667f);
            Check(10, 20f);
            Check(10, 20f);
            Check(5, 0f);
            Check(10, 20f);
            entities.RemoveComponent<SpecialComponent>(mob);
            foreach (var state in states)
                Assert.That((float) thresholds.GetThresholdForState(mob, state),
                    Is.EqualTo(baseline[state]).Within(0.02f));
            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }
}
