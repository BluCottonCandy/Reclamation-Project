using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Explosion.Components;
using Content.Shared.Armor;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Clothing.Loadouts.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class TribalArmorLoadoutTests
{
    // Coefficients retain weaker protection; only excessive blunt/slash resistance is capped.
    private static readonly Dictionary<string, float[]> Expected = new()
    {
        ["N14ClothingOuterTribalArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.80f, 0.85f },
        ["N14ClothingOuterTribalArmorHeavy"] = new[] { 0.75f, 0.75f, 0.75f, 0.75f, 0.80f },
        ["N14ClothingOuterCombatTribal"] = new[] { 0.80f, 0.80f, 0.75f, 0.75f, 0.80f },
        ["N14ClothingOuterBandito"] = new[] { 0.80f, 0.80f, 0.80f, 0.80f, 0.85f },
        ["N14ClothingOuterforager"] = new[] { 0.80f, 0.80f, 0.80f, 0.80f, 0.85f },
        ["N14ClothingOuterheadtaker"] = new[] { 0.80f, 0.80f, 0.80f, 0.80f, 0.85f },
        ["N14ClothingOuterhunter"] = new[] { 0.80f, 0.80f, 0.80f, 0.80f, 0.85f },
        ["N14ClothingOuterponcho"] = new[] { 0.80f, 0.80f, 0.80f, 0.80f, 0.85f },
        ["N14ClothingOutershaman"] = new[] { 0.80f, 0.80f, 0.80f, 0.80f, 0.85f },
        ["N14ClothingOuterdrylandersimple"] = new[] { 0.80f, 0.80f, 0.80f, 0.80f, 0.85f },
        ["N14ClothingOuterTribalCoat"] = new[] { 0.75f, 0.75f, 0.80f, 0.80f, 1.00f },
        ["N14ClothingOuterElderwintercoat"] = new[] { 0.75f, 0.75f, 0.80f, 0.80f, 1.00f },
        ["N14ClothingOuterdrylandelder"] = new[] { 0.75f, 0.75f, 0.80f, 0.80f, 1.00f },
        ["M14ClothingOuterDeadHorsesArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["M14ClothingOuterDeadHorsesLightArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["M14ClothingOuterSorrowsArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["M14ClothingOuterRustwalkersArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["M14ClothingOuterRustwalkersLightArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["M14ClothingOuterWesternWayfarerArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["M14ClothingOuterWesternWayfarerHeavyArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["M14ClothingOuterWardenPlate"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["MisfitsClothingOuterWhiteLegsArmor"] = new[] { 0.80f, 0.80f, 0.80f, 0.78f, 0.92f },
        ["N14ClothingHeadHatTribalOutcastHood"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
        ["N14ClothingDrylanderGoggles"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
        ["N14ClothingHeadTakerwraps"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
        ["N14ClothingTribalHeadWraps"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
        ["N14ClothingTribalHood"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
        ["N14ClothingTribalShamanHat"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
        ["N14ClothingTribalShemagh"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
        ["N14ClothingTribalHunterHood"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
        ["N14ClothingHeadHatTribalPowerhelm"] = new[] { 0.85f, 0.85f, 0.85f, 0.85f, 0.90f },
        ["N14ClothingHeadHatEdlerSkullhelm"] = new[] { 0.90f, 0.90f, 0.90f, 0.90f, 0.95f },
        ["N14ClothingHeadHatEldersDamagedSkullHelm"] = new[] { 0.90f, 0.90f, 0.90f, 0.90f, 0.95f },
        ["MisfitsClothingHeadRadstagHelmet"] = new[] { 0.90f, 0.90f, 0.90f, 0.90f, 0.95f },
        ["N14ClothingDeathclawHelm"] = new[] { 0.80f, 0.85f, 0.85f, 1.00f, 1.00f },
        ["TribalHelmetofPeace"] = new[] { 0.95f, 0.95f, 0.95f, 0.90f, 1.00f },
    };

    [Test]
    public async Task ProtectionIsCappedWithoutBuffingWeakerValues()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var combat = (ArmorComponent) prototypes.Index<EntityPrototype>("N14ClothingOuterCombatArmor").Components["Armor"].Component;
            var types = new[] { "Blunt", "Slash", "Piercing", "Heat" };
            foreach (var (id, expected) in Expected)
            {
                var prototype = prototypes.Index<EntityPrototype>(id);
                var armor = (ArmorComponent) prototype.Components["Armor"].Component;
                Assert.That(prototype.Components.ContainsKey("PowerArmor"), Is.False, id);
                for (var i = 0; i < types.Length; i++)
                    Assert.That(armor.Modifiers.Coefficients.GetValueOrDefault(types[i], 1),
                        Is.EqualTo(expected[i]).Within(0.0001f), id + " " + types[i]);
                foreach (var (type, value) in armor.Modifiers.Coefficients)
                    Assert.That(value, Is.GreaterThanOrEqualTo(combat.Modifiers.Coefficients.GetValueOrDefault(type, 1)), id + " " + type);
                var explosion = prototype.Components.TryGetValue("ExplosionResistance", out var component)
                    ? ((ExplosionResistanceComponent) component.Component).DamageCoefficient : 1;
                Assert.That(explosion, Is.EqualTo(expected[4]).Within(0.0001f), id + " explosion");
                Assert.That(explosion, Is.GreaterThanOrEqualTo(0.8f), id);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryOrdinaryTribalPieceSpawnsThroughWastelanderLoadouts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var job = prototypes.Index<JobPrototype>("Wastelander");
            var loadouts = prototypes.EnumeratePrototypes<LoadoutPrototype>().ToArray();
            foreach (var id in Expected.Keys)
            {
                var available = false;
                foreach (var loadout in loadouts.Where(loadout => loadout.Items.Any(item => item.Id == id)))
                {
                    var human = em.SpawnEntity("MobHuman", map.GridCoords);
                    var profile = HumanoidCharacterProfile.DefaultWithSpecies().WithLoadoutPreference(loadout.ID, true);
                    var (bags, spawned) = em.System<SharedLoadoutSystem>().ApplyCharacterLoadout(
                        human, job, profile, new Dictionary<string, TimeSpan>(), false, out _);
                    available |= spawned.Count == 1;
                    foreach (var item in bags) em.DeleteEntity(item);
                    em.DeleteEntity(human);
                    if (available) break;
                }
                Assert.That(available, Is.True, id + " must spawn for a Wastelander.");
            }
            var added = loadouts.Where(loadout => loadout.ID.StartsWith("ReclamationLoadoutTribal")).ToArray();
            Assert.That(added, Has.Length.EqualTo(26));
            Assert.That(added.All(loadout => loadout.Items.Count == 1 && Expected.ContainsKey(loadout.Items[0].Id)), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
