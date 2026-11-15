using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._Misfits.Genetics.Mutations;
using Content.Shared._ReclamationProject.Nutrition;
using Content.Shared._ReclamationProject.Pack;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture, NonParallelizable]
public sealed class NpcNutritionTest
{
    [Test]
    public async Task WildNpcsLoseNeedsWithoutLosingPassiveRegeneration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var nutrition = entities.System<NpcNutritionSystem>();
        var damageSystem = entities.System<DamageableSystem>();
        var passiveSystem = entities.System<PassiveDamageSystem>();
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId, runMapInit: true);
            try
            {
                var ids = new[] { "N14MobGhoulFeral", "N14MobMirelurk", "N14MobCazador", "N14MobBloatfly",
                    "N14MobRaiderPsycho", "N14MobSuperMutantNPC", "N14MobCentaur", "N14MobRadscorpion" };
                foreach (var id in ids)
                {
                    TestContext.Out.WriteLine("Checking wild NPC: " + id);
                    var mob = entities.SpawnEntity(id, new MapCoordinates(0, 0, mapId));
                    entities.TryGetComponent<PassiveDamageComponent>(mob, out var originalHealing);
                    nutrition.Update(0);
                    Assert.That(entities.HasComponent<HungerComponent>(mob), Is.False, id);
                    Assert.That(entities.HasComponent<ThirstComponent>(mob), Is.False, id);
                    entities.TryGetComponent<PassiveDamageComponent>(mob, out var healing);
                    Assert.That(healing, Is.SameAs(originalHealing), "nutrition must not replace regeneration: " + id);
                    if (healing != null)
                    {
                        var damageable = entities.GetComponent<DamageableComponent>(mob);
                        var type = healing.Damage.DamageDict.FirstOrDefault(entry => entry.Value < 0
                            && damageable.Damage.DamageDict.ContainsKey(entry.Key)).Key;
                        if (type != null)
                        {
                            var damage = new DamageSpecifier();
                            damage.DamageDict[type] = 5;
                            damageSystem.SetDamage(mob, damageable, damage);
                            healing.NextDamage = TimeSpan.Zero;
                            passiveSystem.Update(0);
                            Assert.That(damageable.Damage.DamageDict[type].Float(), Is.LessThan(5), "passive healing: " + id);
                        }
                    }
                    entities.DeleteEntity(mob);
                }
            }
            finally { maps.DeleteMap(mapId); }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrganicPetsPackAnimalsLivestockAndPlayerBodiesKeepWorkingNeeds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var maps = entities.System<SharedMapSystem>();
        var nutrition = entities.System<NpcNutritionSystem>();
        var hungerSystem = entities.System<HungerSystem>();
        var thirstSystem = entities.System<ThirstSystem>();
        await server.WaitAssertion(() =>
        {
            var ids = new HashSet<string> { "MobHuman", "MobCow", "N14MobBrahmin",
                "N14MobDeathclawSentient", "N14MobDeathclawPlayer" };
            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract) continue;
                if (proto.Components.ContainsKey("PackAnimal") || proto.ID.StartsWith("PackMenagerie", StringComparison.Ordinal))
                    ids.Add(proto.ID);
                if (!proto.ID.StartsWith("MisfitsPetSpawner", StringComparison.Ordinal)
                    || !proto.Components.TryGetValue("GhostRoleMobSpawner", out var entry)
                    || entry.Component is not GhostRoleMobSpawnerComponent { Prototype: { } petId }) continue;
                var pet = prototypes.Index(petId);
                // Robot companions use power, not organic food. Every organic perk pet has needs.
                if (pet.Components.ContainsKey("Hunger") || pet.Components.ContainsKey("Thirst")) ids.Add(pet.ID);
            }
            Assert.That(ids.Count, Is.GreaterThan(25), "include pet variants and every Pack/tamed animal");
            maps.CreateMap(out var mapId, runMapInit: true);
            try
            {
                foreach (var id in ids)
                {
                    TestContext.Out.WriteLine("Checking animal/player needs: " + id);
                    // Bwonsamdi's innate blood mutation currently dirties a server-only
                    // Bloodstream component and asserts in Debug. Test this body's nutrition
                    // independently of those unrelated abilities; do not alter its prototype.
                    var overrides = id == "N14MobDeathclawPlayer"
                        ? new ComponentRegistry
                        {
                            ["Mutatable"] = new EntityPrototype.ComponentRegistryEntry(new MutatableComponent()),
                        }
                        : null;
                    var mob = entities.SpawnEntity(id, new MapCoordinates(0, 0, mapId), overrides);
                    nutrition.Update(0);
                    Assert.That(entities.TryGetComponent<HungerComponent>(mob, out var hunger), Is.True, id);
                    Assert.That(entities.TryGetComponent<ThirstComponent>(mob, out var thirst), Is.True, id);
                    Assert.That(hunger!.ActualDecayRate, Is.GreaterThan(0), id);
                    Assert.That(thirst!.ActualDecayRate, Is.GreaterThan(0), id);
                    var food = hunger.CurrentHunger;
                    var water = thirst.CurrentThirst;
                    hungerSystem.Update(0);
                    thirstSystem.Update(0);
                    Assert.That(hunger.CurrentHunger, Is.LessThan(food), "food actually decays: " + id);
                    Assert.That(thirst.CurrentThirst, Is.LessThan(water), "water actually decays: " + id);
                    entities.DeleteEntity(mob);
                }
                var robot = entities.SpawnEntity("N14MobPetEyebot", new MapCoordinates(0, 0, mapId));
                nutrition.Update(0);
                Assert.That(entities.HasComponent<HungerComponent>(robot), Is.False);
                Assert.That(entities.HasComponent<ThirstComponent>(robot), Is.False);
            }
            finally { maps.DeleteMap(mapId); }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BecomingAPackAnimalRestoresNeedsAfterWildExemption()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var nutrition = entities.System<NpcNutritionSystem>();
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId, runMapInit: true);
            try
            {
                var mob = entities.SpawnEntity("N14MobMirelurk", new MapCoordinates(0, 0, mapId));
                nutrition.Update(0);
                Assert.That(entities.HasComponent<HungerComponent>(mob), Is.False);
                Assert.That(entities.HasComponent<ThirstComponent>(mob), Is.False);
                entities.AddComponent<PackAnimalComponent>(mob);
                nutrition.Update(0);
                Assert.That(entities.GetComponent<HungerComponent>(mob).ActualDecayRate, Is.GreaterThan(0));
                Assert.That(entities.GetComponent<ThirstComponent>(mob).ActualDecayRate, Is.GreaterThan(0));
            }
            finally { maps.DeleteMap(mapId); }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExemptionClearsOldNutritionDamageButPreservesInjuries()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var nutrition = entities.System<NpcNutritionSystem>();
        var damageSystem = entities.System<DamageableSystem>();
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId, runMapInit: true);
            try
            {
                var mob = entities.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
                var damageable = entities.GetComponent<DamageableComponent>(mob);
                var damage = new DamageSpecifier();
                damage.DamageDict["Starvation"] = 5;
                damage.DamageDict["Dehydration"] = 6;
                damage.DamageDict["Slash"] = 7;
                damageSystem.SetDamage(mob, damageable, damage);
                // Simulates loading an exempt NPC with old serialized nutrition damage.
                entities.AddComponent<NpcNutritionComponent>(mob);
                nutrition.Update(0);
                Assert.That(damageable.Damage.DamageDict["Starvation"].Float(), Is.Zero);
                Assert.That(damageable.Damage.DamageDict["Dehydration"].Float(), Is.Zero);
                Assert.That(damageable.Damage.DamageDict["Slash"].Float(), Is.EqualTo(7));
            }
            finally { maps.DeleteMap(mapId); }
        });
        await pair.CleanReturnAsync();
    }
}
