using System;
using System.Linq;
using Content.Server.Atmos.Rotting;
using Content.Server.Body.Systems;
using Content.Shared.Body.Organ;
using Content.Server.Temperature.Systems;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Kitchen;
using Content.Server.Kitchen.Components;
using Content.Server.Kitchen.EntitySystems;
using Content.Server.Power.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class CookingPreparationTests
{
    [Test]
    public async Task KnifeCutsWastelandBunIntoTopAndBottom()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default, bun = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            var knife = em.SpawnEntity("KitchenKnife", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, knife), Is.True);
            bun = em.SpawnEntity("N14CookedFoodBreadBun", map.GridCoords);
            var ev = new AfterInteractEvent(user, knife, bun, map.GridCoords, true);
            em.EventBus.RaiseLocalEvent(knife, ev);
            Assert.That(ev.Handled, Is.True);
        });
        await pair.RunTicksSync(150);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(bun), Is.False);
            foreach (var id in new[] { "FoodBreadBunTop", "FoodBreadBunBottom" })
                Assert.That(em.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == id), Is.EqualTo(1));
            em.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ButcheredIguanaDropsItsOwnMeatAndAnimalEyes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default, animal = default, eyes = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            var knife = em.SpawnEntity("KitchenKnife", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, knife), Is.True);
            animal = em.SpawnEntity("N14MobIguana", map.GridCoords);
            eyes = em.System<BodySystem>().GetBodyOrgans(animal).Single(organ => em.GetComponent<MetaDataComponent>(organ.Id).EntityPrototype?.ID == "OrganAnimalEyes").Id;
            em.System<DamageableSystem>().TryChangeDamage(animal, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 100 } }, ignoreResistances: true, doPartDamage: false, canSever: false);
            var ev = new AfterInteractEvent(user, knife, animal, map.GridCoords, true);
            em.EventBus.RaiseLocalEvent(knife, ev);
            Assert.That(ev.Handled, Is.True);
        });
        await pair.RunTicksSync(650);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(animal), Is.False);
            Assert.That(em.EntityQuery<MetaDataComponent>().Any(meta => meta.EntityPrototype?.ID == "N14FoodMeatIguana"), Is.True);
            Assert.That(em.EntityExists(eyes), Is.True);
            Assert.That(em.GetComponent<OrganComponent>(eyes).Body, Is.Null);
            em.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("N14FoodMeatRadCutlet")]
    [TestCase("N14FoodMeatBrahminCutlet")]
    [TestCase("N14FoodMeatRadCutletCooked")]
    [TestCase("N14FoodMeatBrahminCutletCooked")]
    public async Task RawAndCookedCutletsActuallyRotAtRoomTemperature(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid meat = default;
        await pair.Server.WaitAssertion(() =>
        {
            meat = em.SpawnEntity(prototype, map.GridCoords);
            Assert.That(em.GetComponent<PerishableComponent>(meat).RotAfter, Is.EqualTo(TimeSpan.FromMinutes(15)));
            em.System<TemperatureSystem>().ForceChangeTemperature(meat, 293);
            em.System<RottingSystem>().SetRotDelay(meat, TimeSpan.FromSeconds(1));
        });
        await pair.RunTicksSync(360);
        await pair.Server.WaitAssertion(() =>
        {
            if (em.EntityExists(meat))
            {
                Assert.That(em.HasComponent<RottingComponent>(meat), Is.True);
                em.DeleteEntity(meat);
            }
            else
                Assert.That(em.EntityQuery<MetaDataComponent>().Any(meta => meta.EntityPrototype?.ID == "FoodMeatRotten"), Is.True, "Raw cutlets replace themselves with rotten meat.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MicrowaveDoesNotRoastDryScorpionLegAsGenericMeat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var microwave = em.SpawnEntity("KitchenMicrowave", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(microwave).Powered = true;
            var comp = em.GetComponent<MicrowaveComponent>(microwave);
            comp.CurrentCookTimerTime = 20;
            var leg = em.SpawnEntity("N14FoodMeatRadscorpionLeg", map.GridCoords);
            Assert.That(em.System<Robust.Shared.Containers.SharedContainerSystem>().Insert(leg, comp.Storage), Is.True);
            em.System<MicrowaveSystem>().Wzhzhzh(microwave, comp, null);
            Assert.That(em.GetComponent<ActiveMicrowaveComponent>(microwave).PortionedRecipe.Item1, Is.Null);
            em.DeleteEntity(microwave);
        });
        await pair.CleanReturnAsync();
    }
}
