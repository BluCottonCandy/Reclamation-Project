#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Armor;
using Content.Server.Explosion.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Customization.Systems;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class WastelandWardrobeTest
{
    [Test]
    public async Task CatalogMatchesWastelanderClothingAndProtectionPrices()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var groups = new HashSet<string>
            {
                "N14LoadoutHead", "N14LoadoutHands", "N14LoadoutShoes", "N14LoadoutMask",
                "N14LoadoutNeck", "N14LoadoutEyes", "N14LoadoutOuter", "N14LoadoutUniform",
            };
            var expected = new HashSet<string>();
            foreach (var loadout in prototypes.EnumeratePrototypes<LoadoutPrototype>())
            {
                foreach (var item in loadout.Items)
                    Assert.That(item.Id.StartsWith("ClothingUniformJumpsuitColor") ||
                                item.Id.StartsWith("ClothingUniformJumpskirtColor"), Is.False,
                        $"Plain colored outfit is still in {loadout.ID}");
                foreach (var item in loadout.Items)
                    Assert.That(Regex.IsMatch(item.Id, "Galoshes|Cluwne|CatEars|DogEars|GhostSheet", RegexOptions.IgnoreCase),
                        Is.False, $"Removed item is still in {loadout.ID}");

                if (!loadout.Requirements.OfType<CharacterItemGroupRequirement>().Any(r => groups.Contains(r.Group.Id)) ||
                    loadout.Level.ToString() != "None" || loadout.SupporterTier.ToString() != "None")
                    continue;
                if (loadout.Requirements.OfType<CharacterJobRequirement>().Any(r =>
                        r.Jobs.Any(j => j.Id == "Wastelander") == r.Inverted) ||
                    loadout.Requirements.OfType<CharacterDepartmentRequirement>().Any(r =>
                        r.Departments.Any(d => prototypes.Index(d).Roles.Any(j => j.Id == "Wastelander")) == r.Inverted))
                    continue;
                foreach (var item in loadout.Items)
                {
                    var proto = prototypes.Index(item);
                    if (proto.Components.ContainsKey("Clothing") &&
                        !proto.Components.ContainsKey("Gun") && !proto.Components.ContainsKey("MeleeWeapon") &&
                        !proto.Components.ContainsKey("ChameleonClothing"))
                        expected.Add(item.Id);
                }
            }

            var vendor = entities.SpawnEntity("ReclamationWastelanderClothingVendor", map.GridCoords);
            var store = entities.GetComponent<NcStoreComponent>(vendor);
            Assert.That(store.CurrencyWhitelist, Is.EqualTo(new[] { "Caps" }));
            Assert.That(store.SellPresets, Is.Empty);
            Assert.That(store.ContractPresets, Is.Empty);
            Assert.That(store.Listings.Select(l => l.ProductEntity), Is.EquivalentTo(expected));
            Assert.That(store.Listings.Count, Is.EqualTo(expected.Count), "Duplicate listings");
            var outerwear = prototypes.Index<StoreCategoryStructuredPrototype>("ReclamationWardrobeOuter").Entries.Select(e => e.Proto).ToHashSet();
            var mismatches = new List<string>();
            foreach (var listing in store.Listings)
            {
                Assert.That(listing.Cost.Count, Is.EqualTo(1));
                var item = prototypes.Index<EntityPrototype>(listing.ProductEntity);
                var stronger = item.TryGetComponent("Armor", out ArmorComponent? armor) &&
                               armor.Modifiers.Coefficients.Values.Any(v => v < 0.8f);
                stronger |= item.TryGetComponent("ExplosionResistance", out ExplosionResistanceComponent? blast) &&
                            blast.Worn && blast.DamageCoefficient < 0.8f;
                var price = outerwear.Contains(listing.ProductEntity) && stronger ? 50 : 20;
                if (listing.Cost["Caps"] != price)
                    mismatches.Add($"{listing.ProductEntity}: expected {price}, found {listing.Cost["Caps"]}");
            }
            Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches));
            entities.DeleteEntity(vendor);
        });
        await pair.CleanReturnAsync();
    }
}
