using System.Linq;
using Content.Server.Storage.Components;
using Content.Server.UndecidedLoadout.Components;
using Content.Shared.Storage;
using Content.Shared.UndecidedLoadout;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class WastelandTribalKitTests
{
    [TestCase(0, "Kittribalstalker")]
    [TestCase(1, "Kittribalchampion")]
    [TestCase(2, "Kittribalhunter")]
    [TestCase(3, "Kittribalbonebreaker")]
    [TestCase(4, "Kittribalpainbringer")]
    [TestCase(5, "Kittribalfirehand")]
    public async Task ReusesOriginalTribalChoicesAndRedeemsExactlyOne(int selection, string expectedKit)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        EntityUid selector = default;
        await pair.Server.WaitAssertion(() =>
        {
            var master = em.SpawnEntity("ReclamationWastelanderKitSelector", map.GridCoords);
            Assert.That(em.GetComponent<UndecidedLoadoutBackpackComponent>(master).PossibleSets.Select(id => id.ToString()),
                Does.Contain("ReclamationWastelanderSetTribal"));
            var wrapper = em.SpawnEntity("ReclamationWastelanderKitTribal", map.GridCoords);
            var contents = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(wrapper).Items);
            Assert.That(contents, Is.EqualTo(new[] { "Triballoadoutkits" }));
            selector = em.SpawnEntity(contents.Single(), map.GridCoords);
            var component = em.GetComponent<UndecidedLoadoutBackpackComponent>(selector);
            Assert.That(component.MaxSelectedSets, Is.EqualTo(1));
            Assert.That(component.PossibleSets.Select(id => id.ToString()), Is.EqualTo(new[]
            {
                "TribalStalkerSet", "TribalChampionSet", "TribalHunterSet", "TribalBoneBreakerSet", "TribalPainBringerSet", "TribalFireHand"
            }));
            Assert.That(prototypes.Index(component.PossibleSets[selection]).Content.Select(id => id.ToString()),
                Is.EqualTo(new[] { expectedKit }));
            em.EventBus.RaiseLocalEvent(selector, new UndecidedLoadoutBackpackApproveMessage());
            Assert.That(em.HasComponent<UndecidedLoadoutBackpackComponent>(selector), Is.True);
            em.EventBus.RaiseLocalEvent(selector, new UndecidedLoadoutBackpackChangeSetMessage(selection));
            em.EventBus.RaiseLocalEvent(selector, new UndecidedLoadoutBackpackChangeSetMessage((selection + 1) % 6));
            Assert.That(component.SelectedSets, Is.EqualTo(new[] { selection }));
            em.EventBus.RaiseLocalEvent(selector, new UndecidedLoadoutBackpackApproveMessage());
            Assert.That(em.HasComponent<UndecidedLoadoutBackpackComponent>(selector), Is.False);
            // Replayed approval messages must not redeem the choice again.
            em.EventBus.RaiseLocalEvent(selector, new UndecidedLoadoutBackpackApproveMessage());
            var count = 0;
            var query = em.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var metadata))
            {
                if (metadata.EntityPrototype?.ID == expectedKit) count++;
            }
            Assert.That(count, Is.EqualTo(1));
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() => Assert.That(em.EntityExists(selector), Is.False));
        await pair.CleanReturnAsync();
    }
}
