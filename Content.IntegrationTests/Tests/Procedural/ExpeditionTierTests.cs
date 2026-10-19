using System.Linq;
using System.Collections.Generic;
using Content.Server._Misfits.Expeditions;
using Content.Shared._Misfits.Expeditions;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Procedural;

[TestFixture]
public sealed class ExpeditionTierTests
{
    [TestCase(N14ExpeditionCombatTier.Easy, 20)]
    [TestCase(N14ExpeditionCombatTier.Medium, 30)]
    [TestCase(N14ExpeditionCombatTier.Hard, 40)]
    public async Task PopulationAndRareLootRules(N14ExpeditionCombatTier tier, int count)
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var difficulty = prototypes.Index<N14ExpeditionDifficultyPrototype>("N14Expedition" + tier);
            Assert.That(difficulty.CombatTier, Is.EqualTo(tier));
            Assert.That(difficulty.EnemyFamily, Is.Null, "Production difficulties must retain random families");
            Assert.That(difficulty.Maps.Count, Is.EqualTo(5));
            var original = (FixturesComponent) prototypes.Index<EntityPrototype>("N14MobBehemoth").Components["Fixtures"].Component;
            Assert.That(((PhysShapeCircle) original.Fixtures["fix1"].Shape).Radius, Is.EqualTo(0.55f),
                "Ordinary non-expedition behemoths must retain their original collision");
            var families = new HashSet<string>();
            var rareWeapons = new HashSet<string>();
            var weaponRolls = 0;
            var armorRolls = 0;
            var both = 0;
            for (var seed = 0; seed < 10000; seed++)
            {
                var plan = ExpeditionEncounterPlan.Create(tier, seed);
                families.Add(plan.Family);
                Assert.That(plan.Enemies.Length, Is.EqualTo(count));
                Assert.That(plan.Enemies.Count(ExpeditionEncounterPlan.NamedEnemies.Contains), Is.EqualTo(tier == N14ExpeditionCombatTier.Hard ? 1 : 0));
                foreach (var id in plan.Enemies.Distinct())
                {
                    var prototype = prototypes.Index<EntityPrototype>(id);
                    if (plan.Family == "Super mutants")
                    {
                        var fixtures = (FixturesComponent) prototype.Components["Fixtures"].Component;
                        Assert.That(((PhysShapeCircle) fixtures.Fixtures["fix1"].Shape).Radius, Is.EqualTo(0.35f));
                    }
                }
                bool IsRanged(string id) => prototypes.Index<EntityPrototype>(id).Components.ContainsKey("Gun");
                string? Ammo(string id) => prototypes.Index<EntityPrototype>(id).Components.TryGetValue("BasicEntityAmmoProvider", out var component)
                    ? ((BasicEntityAmmoProviderComponent) component.Component).Proto.Id : null;
                var ranged = plan.Enemies.Count(IsRanged);
                var marksmen = plan.Enemies.Count(id => Ammo(id) == "N14Cartridge308Rifle");
                if (tier == N14ExpeditionCombatTier.Easy)
                {
                    Assert.That(plan.Family, Is.Not.EqualTo("Super mutants"));
                    Assert.That(ranged, Is.EqualTo(plan.Family == "Ghouls" ? 0 : 6));
                    Assert.That(marksmen, Is.InRange(0, 2));
                }
                else if (tier == N14ExpeditionCombatTier.Medium)
                {
                    Assert.That(plan.Family, Is.Not.EqualTo("Ghouls"));
                    Assert.That(ranged, Is.EqualTo(15));
                    Assert.That(marksmen, Is.InRange(1, 2));
                    Assert.That(plan.Enemies.Where(IsRanged).Select(Ammo).Distinct().Count(), Is.GreaterThanOrEqualTo(4));
                    Assert.That(plan.Enemies.Where(id => !IsRanged(id)).Distinct().Count(), Is.EqualTo(3));
                    Assert.That(plan.Enemies.Where(IsRanged).Select(Ammo), Does.Not.Contain("N14CartridgePistol10"));
                }
                if (tier == N14ExpeditionCombatTier.Hard)
                {
                    if (plan.Family == "Raiders")
                    {
                        Assert.That(plan.Enemies.Count(id => id is "N14MobRaiderBoss" or "ReclamationExpeditionRaiderLeader"), Is.InRange(3, 5));
                        Assert.That(plan.Enemies.All(id => id is "N14MobRaiderBoss" or "N14MobRaiderHunter" or "ReclamationExpeditionRaiderLeader"), Is.True);
                    }
                    else
                    {
                        Assert.That(plan.Enemies.Count(id => id == "ReclamationExpeditionBehemoth" || id == "ReclamationExpeditionMutantLeader"), Is.EqualTo(1));
                        Assert.That(plan.Enemies.Count(id => id.StartsWith("ReclamationExpeditionNightkin")), Is.EqualTo(2));
                        Assert.That(plan.Enemies.Where(id => id != "ReclamationExpeditionMutantLeader" && !id.StartsWith("ReclamationExpeditionNightkin")).All(id => Ammo(id) == "N14Cartridge308Rifle"), Is.True);
                    }
                }
                if (plan.AdvancedWeapon != null)
                {
                    weaponRolls++;
                    rareWeapons.Add(plan.AdvancedWeapon);
                    Assert.That(prototypes.Index<EntityPrototype>(plan.AdvancedWeapon).Components.ContainsKey("Gun"), Is.True);
                }
                if (plan.AdvancedArmor != null)
                {
                    armorRolls++;
                    Assert.That(plan.AdvancedArmor, Is.EqualTo("N14ClothingOuterCombatArmorMK2"));
                    Assert.That(prototypes.Index<EntityPrototype>(plan.AdvancedArmor).Components.ContainsKey("Armor"), Is.True);
                }
                if (plan.AdvancedArmor != null && plan.AdvancedWeapon != null) both++;
            }
            Assert.That(families.Count, Is.EqualTo(2));
            if (tier == N14ExpeditionCombatTier.Easy)
            {
                Assert.That(weaponRolls + armorRolls, Is.Zero);
                Assert.That(families.Contains("Ghouls") && families.Contains("Raiders"), Is.True);
            }
            else if (tier == N14ExpeditionCombatTier.Hard)
            {
                Assert.That(weaponRolls, Is.EqualTo(10000));
                Assert.That(armorRolls, Is.EqualTo(10000));
                Assert.That(rareWeapons, Is.EquivalentTo(ExpeditionEncounterPlan.AdvancedWeapons));
            }
            else
            {
                Assert.That(weaponRolls, Is.InRange(400, 600));
                Assert.That(armorRolls, Is.InRange(400, 600));
                Assert.That(both, Is.InRange(10, 50), "The two rolls must be independent");
                Assert.That(rareWeapons, Is.EquivalentTo(ExpeditionEncounterPlan.AdvancedWeapons));
            }
        });
        await pair.CleanReturnAsync();
    }
}
