using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client.Lobby.UI;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Clothing.Loadouts.Systems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class PlushieLoadoutTests
{
    private static readonly HashSet<string> Wearable = new()
    {
        "PlushieArachind",
        "PlushieAtmosian",
        "PlushieBee",
        "PlushieDiona",
        "PlushieHampter",
        "PlushieHuman",
        "PlushieLizard",
        "PlushieLizardMirrored",
        "PlushieMoth",
        "PlushieNuke",
        "PlushiePenguin",
        "PlushieRGBee",
        "PlushieRouny",
        "PlushieSlime",
        "PlushieSpaceLizard",
        "PlushieVox",
        "PlushieVulp",
        "PlushieXeno"
    };

    [Test]
    public async Task FreeChoicesAndEditedProfilesStillGiveOnlyOnePlushie()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var category = prototypes.Index<LoadoutCategoryPrototype>("Plushies");
            Assert.That(category.SingleSelection, Is.True);
            Assert.That(category.Root, Is.False, "The dropdown belongs under Uncategorized, not its own tab.");
            Assert.That(category.SelectionIcon?.Id, Is.EqualTo("PlushieLizard"));
            var choices = prototypes.EnumeratePrototypes<LoadoutPrototype>().Where(p => p.Category.Id == "Plushies").ToArray();
            Assert.That(choices.Length, Is.EqualTo(68));
            foreach (var removed in new[] { "PlushieThrongler", "PlushieNuke", "PlushieGhost", "PlushieGhostRevenant" })
                Assert.That(choices.SelectMany(choice => choice.Items).Select(item => item.Id), Does.Not.Contain(removed));
            foreach (var choice in choices)
            {
                Assert.That(choice.Cost, Is.Zero);
                Assert.That(choice.Requirements, Is.Empty);
                Assert.That(choice.Level.ToString(), Is.EqualTo("None"));
                Assert.That(choice.SupporterTier.ToString(), Is.EqualTo("None"));
                Assert.That(choice.AutoEquip, Is.False);
                Assert.That(choice.Items.Count, Is.EqualTo(1));
            }
            var profile = HumanoidCharacterProfile.DefaultWithSpecies()
                .WithLoadoutPreference(choices[0].ID, true)
                .WithLoadoutPreference(choices[1].ID, true);
            var human = em.SpawnEntity("MobHuman", map.GridCoords);
            var (bags, spawned) = em.System<SharedLoadoutSystem>().ApplyCharacterLoadout(
                human, prototypes.Index<JobPrototype>("Wastelander"), profile, new Dictionary<string, TimeSpan>(), false, out _);
            Assert.That(spawned.Count, Is.EqualTo(1));
            Assert.That(bags.Count, Is.EqualTo(1), "Plushie is delivered through normal bag storage, not equipped over headgear.");
            Assert.That(em.System<InventorySystem>().TryGetSlotEntity(human, "head", out _), Is.False);
            foreach (var item in bags) em.DeleteEntity(item);
            em.DeleteEntity(human);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyWizdenPlushiesEquipInTheHeadSlot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var human = em.SpawnEntity("MobHuman", map.GridCoords);
            var inventory = em.System<InventorySystem>();
            foreach (var choice in prototypes.EnumeratePrototypes<LoadoutPrototype>().Where(p => p.Category.Id == "Plushies"))
            {
                var id = choice.Items.Single().Id;
                var plushie = em.SpawnEntity(id, map.GridCoords);
                var allowed = Wearable.Contains(id);
                Assert.That(inventory.CanEquip(human, plushie, "head", out _), Is.EqualTo(allowed), id);
                if (allowed)
                {
                    var clothing = em.GetComponent<ClothingComponent>(plushie);
                    Assert.That(clothing.QuickEquip, Is.False, id);
                    Assert.That(inventory.TryEquip(human, plushie, "head"), Is.True, id);
                    Assert.That(inventory.TryUnequip(human, "head"), Is.True, id);
                }
                em.DeleteEntity(plushie);
            }
            em.DeleteEntity(human);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DropdownHidesAlternativesAndRestoresThemAfterDeselecting()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.ResolveDependency<IEntityManager>();
            var icon = em.SpawnEntity("PlushieLizard", MapCoordinates.Nullspace);
            using var picker = new LoadoutChoicePicker(icon, "Plushies");
            using var first = new Button();
            using var second = new Button();
            var selected = false;
            picker.AddChoice(first, () => selected, () => true);
            picker.AddChoice(second, () => false, () => true);
            Assert.That(picker.Choices.Visible, Is.False);
            picker.Expanded = true;
            Assert.That(first.Visible && second.Visible, Is.True);
            selected = true;
            picker.Refresh();
            Assert.That(first.Visible, Is.True);
            Assert.That(second.Visible, Is.False);
            selected = false;
            picker.Refresh();
            Assert.That(first.Visible && second.Visible, Is.True);
            picker.Expanded = false;
            Assert.That(picker.Choices.Visible, Is.False);
            em.DeleteEntity(icon);
        });
        await pair.CleanReturnAsync();
    }
}
