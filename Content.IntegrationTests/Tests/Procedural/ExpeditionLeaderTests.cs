using System.Linq;
using System.Numerics;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Dataset;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radiation.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Procedural;

[TestFixture]
public sealed class ExpeditionLeaderTests
{
    [Test]
    public async Task NamedLeadersHaveProtectionRadiationAndLockedArmor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var mutant = entities.SpawnEntity("ReclamationExpeditionMutantLeader", map.GridCoords.Offset(new Vector2(19, 0)));
            var ghoul = entities.SpawnEntity("ReclamationExpeditionGlowingLeader", map.GridCoords.Offset(new Vector2(10, 0)));
            var raider = entities.SpawnEntity("ReclamationExpeditionRaiderLeader", map.GridCoords.Offset(new Vector2(20, 0)));
            var damage = entities.System<DamageableSystem>();
            damage.TryChangeDamage(mutant, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 20));
            Assert.That((float) entities.GetComponent<DamageableComponent>(mutant).TotalDamage, Is.EqualTo(10));
            foreach (var (uid, dataset) in new[]
                     {
                         (mutant, "ReclamationExpeditionMutantNames"),
                         (ghoul, "ReclamationExpeditionGhoulNames"),
                         (raider, "ReclamationExpeditionRaiderNames")
                     })
                Assert.That(prototypes.Index<DatasetPrototype>(dataset).Values,
                    Does.Contain(entities.GetComponent<MetaDataComponent>(uid).EntityName));

            var thresholds = entities.GetComponent<MobThresholdsComponent>(ghoul).Thresholds;
            Assert.That((float) thresholds.Single(t => t.Value == MobState.Dead).Key, Is.EqualTo(200));
            Assert.That((float) entities.GetComponent<MeleeWeaponComponent>(ghoul).Damage.DamageDict["Radiation"], Is.GreaterThan(0));
            Assert.That(entities.GetComponent<RadiationSourceComponent>(ghoul).Intensity, Is.GreaterThan(0));

            var inventory = entities.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(raider, "outerClothing", out var armor), Is.True);
            Assert.That(entities.GetComponent<MetaDataComponent>(armor!.Value).EntityPrototype!.ID,
                Is.EqualTo("ReclamationExpeditionRaiderPowerArmor"));
            foreach (var slot in new[] { "head", "gloves", "shoes" })
            {
                Assert.That(inventory.TryGetSlotEntity(raider, slot, out var part), Is.True);
                Assert.That(entities.HasComponent<UnremoveableComponent>(part!.Value), Is.True);
                Assert.That(inventory.TryUnequip(raider, slot), Is.False);
            }
            Assert.That(prototypes.Index<EntityPrototype>("ReclamationExpeditionMutantLeader").Parents,
                Does.Contain("ReclamationExpeditionBehemoth"));
            Assert.That(entities.GetComponent<Robust.Server.GameObjects.PointLightComponent>(raider).Enabled, Is.True);
            var salvaged = prototypes.Index<EntityPrototype>("N14ClothingOuterPowerArmorT45NCRSalvaged");
            var normalIntegrity = (PowerArmorIntegrityComponent) salvaged.Components["PowerArmorIntegrity"].Component;
            Assert.That((float) entities.GetComponent<PowerArmorIntegrityComponent>(armor.Value).MaxIntegrity,
                Is.EqualTo((float) normalIntegrity.MaxIntegrity / 2));
            var normalArmor = (ArmorComponent) salvaged.Components["Armor"].Component;
            Assert.That(entities.GetComponent<ArmorComponent>(armor.Value).Modifiers.Coefficients,
                Is.EquivalentTo(normalArmor.Modifiers.Coefficients));
            Assert.That(entities.HasComponent<UnremoveableComponent>(armor.Value), Is.True);
            Assert.That(inventory.TryUnequip(raider, "outerClothing"), Is.False);

            damage.TryChangeDamage(raider, new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Piercing"), 5000), ignoreResistances: true);
            Assert.That(entities.System<MobStateSystem>().IsDead(raider), Is.True);
            Assert.That(inventory.TryUnequip(mutant, raider, "outerClothing"), Is.False,
                "The named raider's corpse must not provide removable power armor");
            Assert.That(inventory.TryGetSlotEntity(raider, "outerClothing", out var retained), Is.True);
            Assert.That(retained, Is.EqualTo(armor));
            foreach (var slot in new[] { "head", "gloves", "shoes" })
                Assert.That(inventory.TryUnequip(mutant, raider, slot), Is.False);

            entities.DeleteEntity(mutant);
            entities.DeleteEntity(ghoul);
            entities.DeleteEntity(raider);
        });
        await pair.CleanReturnAsync();
    }
}
