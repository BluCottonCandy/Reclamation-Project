using Content.Server.Mind;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.Roles;
using Content.Shared.Mind;
using Content.Shared.Objectives.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class ActivatedSynthObjectiveTest
{
    [Test]
    public async Task ActivationAssignsExactlyTwoDifferentObjectives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var minds = entities.System<MindSystem>();
            var roles = entities.System<RoleSystem>();
            var rule = entities.SpawnEntity("ActivatedSynths", map.GridCoords);
            var selection = entities.GetComponent<AntagSelectionComponent>(rule);
            for (var i = 0; i < 20; i++)
            {
                var body = entities.SpawnEntity(null, map.GridCoords);
                var mind = minds.CreateMind(null);
                minds.TransferTo(mind, body, mind: mind.Comp);
                roles.MindAddRole(mind, new TraitorRoleComponent { PrototypeId = "ActivatedSynth" }, mind.Comp, true);
                var ev = new AfterAntagEntitySelectedEvent(null, body, (rule, selection), selection.Definitions[0]);
                entities.EventBus.RaiseLocalEvent(rule, ref ev, true);
                Assert.That(mind.Comp.Objectives.Count, Is.EqualTo(2));
                var first = entities.GetComponent<MetaDataComponent>(mind.Comp.Objectives[0]).EntityPrototype!.ID;
                var second = entities.GetComponent<MetaDataComponent>(mind.Comp.Objectives[1]).EntityPrototype!.ID;
                Assert.That(first, Is.Not.EqualTo(second));
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NestedInventoryRequiresFourDifferentFactionKeys()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var containers = entities.System<SharedContainerSystem>();
            var minds = entities.System<MindSystem>();
            var objectives = entities.System<SharedObjectivesSystem>();
            var wearer = entities.SpawnEntity(null, map.GridCoords);
            var bag = entities.SpawnEntity(null, map.GridCoords);
            var pockets = containers.EnsureContainer<Container>(wearer, "pockets");
            var contents = containers.EnsureContainer<Container>(bag, "contents");
            Assert.That(containers.Insert(bag, pockets), Is.True);
            var mind = minds.CreateMind(null);
            minds.TransferTo(mind, wearer, mind: mind.Comp);
            var objective = entities.SpawnEntity("SynthKeysObjective", map.GridCoords);
            for (var i = 0; i < 4; i++)
                Assert.That(containers.Insert(entities.SpawnEntity("EncryptionKeyMinutemen", map.GridCoords), contents), Is.True);
            Assert.That(objectives.GetProgress(objective, mind), Is.EqualTo(0.25f));
            foreach (var key in new[] { "EncryptionKeyBrotherhoodOfSteel", "EncryptionKeyTown", "EncryptionKeyRailroad" })
                Assert.That(containers.Insert(entities.SpawnEntity(key, map.GridCoords), contents), Is.True);
            Assert.That(objectives.GetProgress(objective, mind), Is.EqualTo(1f));
            Assert.That(containers.Remove(bag, pockets), Is.True);
            Assert.That(objectives.GetProgress(objective, mind), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FourHolodisksAndOnlyTheStandardRifleCount()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var containers = entities.System<SharedContainerSystem>();
            var minds = entities.System<MindSystem>();
            var objectives = entities.System<SharedObjectivesSystem>();
            var wearer = entities.SpawnEntity(null, map.GridCoords);
            var inventory = containers.EnsureContainer<Container>(wearer, "inventory");
            var mind = minds.CreateMind(null);
            minds.TransferTo(mind, wearer, mind: mind.Comp);
            var disks = entities.SpawnEntity("SynthHolodisksObjective", map.GridCoords);
            for (var i = 0; i < 4; i++)
            {
                Assert.That(containers.Insert(entities.SpawnEntity("N14Holotape", map.GridCoords), inventory), Is.True);
                Assert.That(objectives.GetProgress(disks, mind), Is.EqualTo((i + 1) / 4f));
            }
            var rifle = entities.SpawnEntity("SynthRifleObjective", map.GridCoords);
            Assert.That(containers.Insert(entities.SpawnEntity("N14WeaponLaserRifleMakeshift", map.GridCoords), inventory), Is.True);
            Assert.That(objectives.GetProgress(rifle, mind), Is.Zero);
            Assert.That(containers.Insert(entities.SpawnEntity("N14WeaponLaserRifle", map.GridCoords), inventory), Is.True);
            Assert.That(objectives.GetProgress(rifle, mind), Is.EqualTo(1f));
        });
        await pair.CleanReturnAsync();
    }
}
