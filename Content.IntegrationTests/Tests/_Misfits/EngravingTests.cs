// #Misfits Add: Integration tests for one-time sharp-object item engraving.
#nullable enable

using System.Linq;
using Content.Server._Misfits.Engraving;
using Content.Shared.Hands.Components;
using Content.Shared._Misfits.Engraving;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Misfits;

[TestFixture]
public sealed class EngravingTests
{
    [Test]
    public async Task EngravingNamesDescribesOwnsAndLocksItem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entityManager = server.ResolveDependency<IEntityManager>();

        EntityUid user = default;
        EntityUid target = default;
        EntityUid sharp = default;
        MetaDataComponent? metadata = null;
        EngravedComponent? engraved = null;
        string ownerName = string.Empty;

        await server.WaitAssertion(() =>
        {
            var engraving = entityManager.System<EngravingSystem>();
            var metadataSystem = entityManager.System<MetaDataSystem>();

            user = entityManager.SpawnEntity("MobHumanDummy", map.GridCoords);
            target = entityManager.SpawnEntity("N14KitchenKnife", map.GridCoords);
            sharp = entityManager.SpawnEntity("N14CombatKnife", map.GridCoords);
            metadataSystem.SetEntityName(user, "Test Engraver");
            ownerName = entityManager.GetComponent<MetaDataComponent>(user).EntityName;

            Assert.That(entityManager.HasComponent<EngravableComponent>(target), Is.True);

            var clickInteraction = new AfterInteractUsingEvent(user, sharp, target, map.GridCoords, true);
            entityManager.EventBus.RaiseLocalEvent(target, clickInteraction);
            Assert.That(clickInteraction.Handled, Is.False);

            var hands = entityManager.GetComponent<HandsComponent>(user);
            var verbs = new GetVerbsEvent<UtilityVerb>(user, target, sharp, hands, true, true, true, []);
            entityManager.EventBus.RaiseLocalEvent(sharp, verbs, true);
            Assert.That(verbs.Verbs.Any(verb => verb.Text == "Engrave"), Is.True);

            Assert.That(engraving.TryApplyEngraving(user, target, "[color=red]Lucky[/color]", "[bold]Cuts clean.[/bold]"), Is.True);

            metadata = entityManager.GetComponent<MetaDataComponent>(target);
            Assert.Multiple(() =>
            {
                Assert.That(metadata.EntityName, Is.EqualTo("Lucky"));
                Assert.That(metadata.EntityDescription, Is.EqualTo("Cuts clean."));
                Assert.That(entityManager.TryGetComponent(target, out engraved), Is.True);
                Assert.That(engraved!.OwnerName, Is.EqualTo(ownerName));
            });

            Assert.That(engraving.TryApplyEngraving(user, target, "Second Name", "Second description."), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(metadata.EntityName, Is.EqualTo("Lucky"));
                Assert.That(metadata.EntityDescription, Is.EqualTo("Cuts clean."));
                Assert.That(engraved!.OwnerName, Is.EqualTo(ownerName));
            });

            var engravedVerbs = new GetVerbsEvent<UtilityVerb>(user, target, sharp, hands, true, true, true, []);
            entityManager.EventBus.RaiseLocalEvent(sharp, engravedVerbs, true);
            Assert.That(engravedVerbs.Verbs.Any(verb => verb.Text == "Engrave"), Is.False);
        });

        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var examine = entityManager.System<ExamineSystemShared>();
            var examineText = examine.GetExamineText(target, user).ToMarkup();
            Assert.That(examineText, Does.Contain("Belongs to"));
            Assert.That(examineText, Does.Contain("palegreen"));
            Assert.That(examineText, Does.Contain(ownerName));
        });

        await pair.CleanReturnAsync();
    }
}
