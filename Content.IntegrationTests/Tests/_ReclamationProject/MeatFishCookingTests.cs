using System.Linq;
using Content.Server.Construction.Components;
using Content.Server.Nutrition.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Kitchen;
using Content.Shared.Temperature;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class MeatFishCookingTests
{
    [TestCase("N14FoodMeatRadhogCooked", "porkchop_roasted")]
    [TestCase("N14FoodMeatPigratCooked", "morsels_roasted")]
    public async Task CookedMeatUsesItsOwnVisibleShape(string prototype, string expectedState)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        NetEntity net = default;
        await pair.Server.WaitPost(() => net = server.GetNetEntity(server.SpawnEntity(prototype, map.GridCoords)));
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.ResolveDependency<IEntityManager>();
            var visible = em.GetComponent<SpriteComponent>(em.GetEntity(net)).AllLayers
                .OfType<SpriteComponent.Layer>().Where(layer => layer.Visible).ToArray();
            Assert.That(visible, Has.Length.EqualTo(1));
            Assert.That(visible[0].State.ToString(), Is.EqualTo(expectedState));
        });
        await pair.Server.WaitPost(() => server.DeleteEntity(server.GetEntity(net)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DryHeatCannotBypassRadscorpionBoilingWater()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid leg = default;
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var recipe = prototypes.Index<FoodRecipePrototype>("N14RecipeRadscorpionLeg");
            Assert.That(recipe.IngredientsReagents["Water"].Float(), Is.EqualTo(15));
            Assert.That(prototypes.Index<ConstructionGraphPrototype>("MeatRadscorpionLeg").Nodes["start"].Edges, Is.Empty);
            leg = em.SpawnEntity("N14FoodMeatRadscorpionLeg", map.GridCoords);
            em.System<TemperatureSystem>().ForceChangeTemperature(leg, 400);
            em.System<TemperatureSystem>().ConductToInternalTemperature(leg, 10000);
            Assert.That(em.GetComponent<InternalTemperatureComponent>(leg).Temperature, Is.GreaterThan(305));
            em.EventBus.RaiseLocalEvent(leg, new OnTemperatureChangeEvent(400, 290, 110));
        });
        await pair.RunTicksSync(120);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(leg), Is.True, "A dry heated leg must not turn into the boiled result.");
            Assert.That(em.GetComponent<ConstructionComponent>(leg).Node, Is.EqualTo("start"));
            em.DeleteEntity(leg);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KnifeSlicesCookedSupermutantMeatIntoThreeCookedCutlets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid meat = default, user = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            var knife = em.SpawnEntity("KitchenKnife", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, knife), Is.True);
            meat = em.SpawnEntity("N14FoodMeatSupermutantCooked", map.GridCoords);
            Assert.That(em.GetComponent<SliceableFoodComponent>(meat).TotalCount, Is.EqualTo(3));
            var interaction = new InteractUsingEvent(user, knife, meat, map.GridCoords);
            em.EventBus.RaiseLocalEvent(meat, interaction);
            Assert.That(interaction.Handled, Is.True);
        });
        await pair.RunTicksSync(150);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(meat), Is.False);
            Assert.That(em.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "N14FoodMeatSupermutantCutletCooked"), Is.EqualTo(3));
            em.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }
}
