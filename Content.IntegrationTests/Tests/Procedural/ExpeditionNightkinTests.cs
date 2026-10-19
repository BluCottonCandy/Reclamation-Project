using Content.Server._Misfits.Expeditions;
using Content.Shared._Misfits.Expeditions;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Procedural;

[TestFixture]
public sealed class ExpeditionNightkinTests
{
    [TestCase("ReclamationExpeditionNightkinVeteran")]
    [TestCase("ReclamationExpeditionNightkinElite")]
    public async Task ExpeditionNightkinKeepVisibleSilhouettes(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        EntityUid npc = default;
        NetEntity net = default;
        await pair.Server.WaitAssertion(() =>
        {
            npc = server.SpawnEntity(prototype, map.GridCoords);
            net = server.GetNetEntity(npc);
            Assert.That(server.HasComponent<StealthComponent>(npc), Is.False);
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var entity = client.GetEntity(net);
            var sprite = client.GetComponent<SpriteComponent>(entity);
            Assert.That(sprite.Visible && sprite[0].Visible, Is.True);
            Assert.That(sprite.Color.A * sprite[0].Color.A, Is.GreaterThanOrEqualTo(0.7f));
            Assert.That(client.HasComponent<StealthComponent>(entity), Is.False);
        });
        await pair.Server.WaitPost(() => server.DeleteEntity(npc));
        await pair.CleanReturnAsync();
    }

    [Test]
    public void FamilyOverridesCannotBypassDifficultyRestrictions()
    {
        Assert.Throws<ArgumentException>(() => ExpeditionEncounterPlan.Create(
            N14ExpeditionCombatTier.Easy, 42, N14ExpeditionEnemyFamily.SuperMutants));
        Assert.Throws<ArgumentException>(() => ExpeditionEncounterPlan.Create(
            N14ExpeditionCombatTier.Medium, 42, N14ExpeditionEnemyFamily.Ghouls));
        Assert.Throws<ArgumentException>(() => ExpeditionEncounterPlan.Create(
            N14ExpeditionCombatTier.Hard, 42, N14ExpeditionEnemyFamily.Ghouls));
    }
}
